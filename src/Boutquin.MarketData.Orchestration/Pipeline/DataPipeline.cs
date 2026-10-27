// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License").
//  You may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//  See the License for the specific language governing permissions and
//  limitations under the License.
//

using System.Security.Cryptography;
using System.Text;

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Abstractions.Snapshots;
using Boutquin.MarketData.Cache.Keys;
using Boutquin.MarketData.Cache.Policies;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.Cache.WriteThrough;
using Boutquin.MarketData.Orchestration.Coverage;
using Boutquin.MarketData.Orchestration.Fallback;
using Boutquin.MarketData.Orchestration.Snapshots;
using Boutquin.MarketData.Storage.Checkpointing;
using Boutquin.MarketData.Storage.Events;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.Storage.Domain.Exceptions;
using Boutquin.Storage.Domain.ValueObjects;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boutquin.MarketData.Orchestration.Pipeline;

/// <summary>
/// Orchestrates the end-to-end data fetch pipeline: L1 cache check, L2 snapshot check,
/// adapter fallback with per-partition checkpointing, normalization, coverage computation,
/// snapshot persistence, and L1 promotion.
/// </summary>
/// <remarks>
/// <para>
/// Tier strategy: L1 is an in-process memory cache (hot, volatile, per-process). L2 is a
/// durable snapshot store (warm, cross-process, survives restart). Live fetch via the adapter
/// fallback chain is the authoritative source. Reads cascade L1 → L2 → Live; successful live
/// fetches write back to L2 and promote to L1 so the next request of the same shape is served
/// from memory.
/// </para>
/// <para>
/// Checkpointing: batch requests (those implementing <see cref="IBatchDataRequest{TPartitionKey}"/>)
/// persist the highest contiguously successful partition index under an identity derived from the
/// request type, record type, logical dataset key, frequency, and range. The legacy path writes its
/// aggregate snapshot only after the whole fetch loop, so a checkpoint cannot restore a partial
/// payload atomically. A retry therefore restarts at partition zero and rebuilds the complete
/// envelope rather than returning only the uncheckpointed suffix.
/// </para>
/// <para>
/// Generic composition: per-call services (adapters, evaluator, serializer, normalizer,
/// partitioner) are resolved from <see cref="IServiceProvider"/> at <c>ExecuteAsync</c> time
/// because <c>TRequest</c>/<c>TRecord</c> are not known at pipeline construction.
/// </para>
/// <para>
/// Concurrency: two calls for the same cache key issued concurrently may both miss L1/L2 and
/// race to fetch. Wrap the pipeline with an exact-once adapter such as
/// <c>LazyAsyncCache</c> when thundering-herd coalescing is required.
/// </para>
/// </remarks>
public sealed class DataPipeline : IDataPipeline
{
    // UTF-8 identities at or below this byte count use a stack buffer. Larger identities use a
    // heap-backed byte array so hostile or unusually large logical keys cannot grow stack usage.
    private const int MaxStackAllocatedCheckpointIdentityBytes = 512;

    // Snapshots are written with this version stamp. Bump when the persisted record layout
    // changes in a way readers must branch on (new required field, changed semantics of an
    // existing field, units change). Do not bump for additive, ignorable changes — readers
    // tolerate extra metadata. When multi-version read support is needed, replace this
    // constant with an ISchemaRegistry lookup keyed by dataset.
    private static readonly SchemaVersion s_currentSnapshotSchemaVersion = new(1);

    private readonly ILocalCache _localCache;
    private readonly ISnapshotStore _snapshotStore;
    private readonly IngestionCheckpointStore _checkpointStore;
    private readonly IngestionEventLog _eventLog;
    private readonly SnapshotStalenessPolicy _stalenessPolicy;
    private readonly CoverageCalculator _coverageCalculator;
    private readonly IOptions<DataPipelineOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly IClock _clock;
    private readonly ILogger<DataPipeline> _logger;

    /// <summary>
    /// Initializes a new <see cref="DataPipeline"/>.
    /// </summary>
    public DataPipeline(
        ILocalCache localCache,
        ISnapshotStore snapshotStore,
        IngestionCheckpointStore checkpointStore,
        IngestionEventLog eventLog,
        SnapshotStalenessPolicy stalenessPolicy,
        CoverageCalculator coverageCalculator,
        IOptions<DataPipelineOptions> options,
        IServiceProvider serviceProvider,
        IClock clock,
        ILogger<DataPipeline> logger)
    {
        _localCache = localCache;
        _snapshotStore = snapshotStore;
        _checkpointStore = checkpointStore;
        _eventLog = eventLog;
        _stalenessPolicy = stalenessPolicy;
        _coverageCalculator = coverageCalculator;
        _options = options;
        _serviceProvider = serviceProvider;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IDataSnapshot> AcquireReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The time-series store registry is the source of per-record engines whose
        // AcquireSnapshotAsync pins a point-in-time view. It is registered
        // by AddMarketDataKernel / AddTimeSeriesStore; a snapshot read requires it to be present.
        var registry = _serviceProvider.GetService<ITimeSeriesStoreRegistry>()
            ?? throw new InvalidOperationException(
                "AcquireReadSnapshotAsync requires an ITimeSeriesStoreRegistry in the container. "
                + "Register the time-series store tier via AddMarketDataKernel (or AddTimeSeriesStore) "
                + "before acquiring a read snapshot.");

        // DataSnapshot resolves a per-record ISnapshotReadBinding<TRecord> lazily on first read,
        // keyed by the record type. The binding owns the (TDatasetKey, engine, dataset-key build,
        // range read, gap detection) machinery so the IDataSnapshot.ExecuteAsync<TRecord> surface
        // need not carry TDatasetKey.
        IDataSnapshot snapshot = new DataSnapshot(
            registry,
            recordType => _serviceProvider.GetService(
                typeof(ISnapshotReadBinding<>).MakeGenericType(recordType)));

        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public async Task<DataEnvelope<IReadOnlyList<TRecord>>> ExecuteAsync<TRequest, TRecord>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IDataRequest
    {
        ArgumentNullException.ThrowIfNull(request);

        // Step 1: Build cache key. Record type + frequency + dataset + date range forms the identity so
        // a request for 2026-Q1 does not shadow an earlier request for 2025-Q4 on the same
        // dataset. Including typeof(TRecord).Name also keeps parallel requests for different
        // record shapes on the same dataset (e.g. Bar vs FxRate) in disjoint cache entries.
        var cacheKey = CacheKeyBuilder.FromDateRange(
            $"pipeline-v2/{typeof(TRecord).Name}/{request.Frequency}",
            request.DatasetKey,
            request.Range.From,
            request.Range.To);

        // Step 2: Check L1 (hot in-process cache). On hit we rewrite provenance to
        // RetrievalMode="cache" and drop SourceUrl so downstream consumers can distinguish a
        // cached replay from a fresh fetch for audit/billing purposes — the original source URL
        // applied to the live fetch, not to this replay.
        var cached = await _localCache
            .GetAsync<DataEnvelope<IReadOnlyList<TRecord>>>(cacheKey, cancellationToken)
            .ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogDebug("L1 cache hit for {CacheKey}", cacheKey);
            var cacheProvenance = cached.Provenance
                .Select(p => p with { RetrievalMode = RetrievalMode.Cache, SourceUrl = null })
                .ToList();
            return new DataEnvelope<IReadOnlyList<TRecord>>(
                cached.Payload,
                cached.Coverage,
                cached.Issues,
                cacheProvenance);
        }

        // Step 2b: time-series store path, gated on the preview flag. When on AND a
        // dataset-key builder + live store are registered for this request's record type, the
        // rewritten gap-fill flow runs and returns here; the legacy L2 path below is skipped
        // entirely. When the flag is off — or the request type is not time-series-backed — this
        // returns null and control falls through to the unchanged legacy path (AC-7).
        IReadOnlyList<TRequest>? legacyPartitionsOverride = null;
        if (_options.Value.UseTimeSeriesStore)
        {
            var registry = _serviceProvider.GetService<ITimeSeriesStoreRegistry>();
            if (registry is not null)
            {
                var attemptedRanges = _serviceProvider.GetService<IAttemptedRangeIndex>()
                    ?? new InMemoryAttemptedRangeIndex();
                var executor = new TimeSeriesPipelineExecutor(
                    _serviceProvider, registry, _localCache, attemptedRanges, _options.Value, _clock, _logger);

                // Consumer partitioning is authoritative in BOTH paths. The reflected splitter is
                // only the default for a batch request with no consumer policy, so toggling the
                // time-series option cannot silently change dispatch shape (C-072).
                var timeSeriesPartitioner = _serviceProvider.GetService<IRequestPartitioner<TRequest>>();
                IReadOnlyList<TRequest>? partitionRequests = timeSeriesPartitioner?.Partition(request);
                if (partitionRequests is null
                    && BatchRequestSplitter.TrySplit(request, out var defaultPartitions))
                {
                    partitionRequests = defaultPartitions;
                }

                if (partitionRequests is not null)
                {
                    // DividendPayment and Bar have no row-level symbol. A consumer-preserved
                    // multi-symbol partition cannot be assigned to any physical symbol series.
                    // Reuse the already-selected partition list in the legacy path so the consumer
                    // policy runs exactly once and no ambiguous row reaches the time-series store.
                    var requiresLegacySymbolDispatch =
                        (typeof(TRequest) == typeof(DividendHistoryRequest)
                            && typeof(TRecord) == typeof(DividendPayment)
                            && partitionRequests.Cast<DividendHistoryRequest>()
                                .Any(partition => partition.Symbols.Count != 1))
                        || (typeof(TRequest) == typeof(PriceHistoryRequest)
                            && typeof(TRecord) == typeof(Bar)
                            && partitionRequests.Cast<PriceHistoryRequest>()
                                .Any(partition => partition.Symbols.Count != 1));
                    if (requiresLegacySymbolDispatch)
                    {
                        legacyPartitionsOverride = partitionRequests;
                    }
                    else
                    {
                        var partitionedEnvelope = await RunPartitionedTimeSeriesAsync<TRequest, TRecord>(
                            executor, partitionRequests, cancellationToken).ConfigureAwait(false);
                        if (partitionedEnvelope is not null)
                        {
                            return partitionedEnvelope;
                        }
                    }
                    // At least one partition's request type is not time-series-backed — fall through
                    // to the legacy path for the whole request (all partitions share a type, so this
                    // is all-or-nothing).
                }
                else
                {
                    var tsEnvelope = await executor
                        .TryRunAsync<TRequest, TRecord>(request, cacheKey, cancellationToken)
                        .ConfigureAwait(false);
                    if (tsEnvelope is not null)
                    {
                        return tsEnvelope;
                    }
                }
            }
            else
            {
                _logger.LogWarning(
                    "UseTimeSeriesStore is enabled but no ITimeSeriesStoreRegistry is registered; "
                    + "falling back to the legacy L2 path. Register the time-series tier via "
                    + "AddMarketDataKernel to use the store-backed pipeline.");
            }
        }

        // Step 3: Check L2 (durable snapshot, survives restart). WriteThroughCache is optional
        // because test harnesses and ephemeral pipelines may skip L2 entirely; when absent we
        // fall straight through to the live adapter chain.
        var writeThrough = _serviceProvider
            .GetService<WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<TRecord>>>>();

        if (writeThrough is not null)
        {
            var snapshot = await writeThrough
                .TryReadAsync(cacheKey, cancellationToken)
                .ConfigureAwait(false);

            if (snapshot is not null)
            {
                var staleness = _stalenessPolicy.CheckStaleness(cacheKey, snapshot.Metadata.StoredAtUtc);
                if (staleness is null)
                {
                    _logger.LogDebug("L2 snapshot hit (fresh) for {CacheKey}", cacheKey);
                    // Update provenance to reflect that data was served from L2 snapshot, not live API
                    var snapshotProvenance = snapshot.Metadata.Provenance
                        .Select(p => p with { RetrievalMode = RetrievalMode.Snapshot, SourceUrl = null })
                        .ToList();
                    var snapshotCoverage = snapshot.Metadata.Coverage
                        ?? (request is DividendHistoryRequest
                            && typeof(TRecord) == typeof(DividendPayment)
                            && snapshot.Metadata.Issues.All(issue => issue.Severity != IssueSeverity.Error)
                                ? new DataCoverage(
                                    snapshot.Payload.Count,
                                    snapshot.Payload.Count,
                                    MissingPoints: 0,
                                    CoverageRatio: 1.0m)
                                : _coverageCalculator.Compute(
                                    request.Range,
                                    request.Frequency,
                                    snapshot.Payload.Count));
                    var snapshotEnvelope = new DataEnvelope<IReadOnlyList<TRecord>>(
                        snapshot.Payload,
                        snapshotCoverage,
                        snapshot.Metadata.Issues,
                        snapshotProvenance);
                    await _localCache
                        .SetAsync(cacheKey, snapshotEnvelope, _options.Value.L1CacheTtl, cancellationToken)
                        .ConfigureAwait(false);
                    return snapshotEnvelope;
                }

                _logger.LogInformation("L2 snapshot stale for {CacheKey}: {Issue}", cacheKey, staleness.Message);
            }
        }

        // Step 4: Load checkpoint (batch requests only). The legacy path has no durable partial
        // payload that can reconstruct the checkpointed prefix: L2 is written only after the full
        // loop. Therefore a checkpoint is a durable progress boundary, but recovery safely starts
        // from partition zero so the returned envelope and final snapshot retain the whole prefix.
        var startIndex = 0;
        string? pipelineId = null;
        // Fast-path pattern match using object as the partition-key generic parameter — this
        // succeeds for any IBatchDataRequest<T> whose T is a reference type thanks to variance.
        // For value-type partition keys the cast fails and we fall back to a reflection scan.
        var isBatch = request is IBatchDataRequest<object>;
        if (!isBatch)
        {
            isBatch = IsBatchRequest(request);
        }

        if (isBatch)
        {
            pipelineId = BuildCheckpointProcessorId<TRequest, TRecord>(request);
            var checkpoint = await _checkpointStore
                .LoadAsync(pipelineId, cancellationToken)
                .ConfigureAwait(false);
            if (checkpoint is not null)
            {
                _logger.LogInformation(
                    "Checkpoint at index {Index} found for {PipelineId}; restarting at partition zero "
                    + "because the legacy snapshot tier has no atomically persisted prefix payload.",
                    checkpoint.SymbolIndex,
                    pipelineId);
            }
        }

        // Step 5: Partition
        var partitions = legacyPartitionsOverride;
        if (partitions is null)
        {
            var partitioner = _serviceProvider.GetService<IRequestPartitioner<TRequest>>();
            partitions = partitioner?.Partition(request)
                ?? (BatchRequestSplitter.TrySplit(request, out var defaultPartitions) ? defaultPartitions : [request]);
        }

        // Step 6: Fetch loop
        var allRecords = new List<TRecord>();
        var allProvenance = new List<DataProvenance>();
        var allIssues = new List<DataIssue>();
        var adapterCoverages = new List<DataCoverage>();
        var partitionFailed = false;
        var nextContiguousCheckpointIndex = 0;

        var adapters = _serviceProvider
            .GetServices<IDataSourceAdapter<TRequest, TRecord>>()
            .ToList();
        var evaluator = _serviceProvider
            .GetService<IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<TRecord>>>>()
            ?? new CoverageResponseEvaluator<TRecord>();
        var normalizer = _serviceProvider.GetService<INormalizer<TRecord, TRecord>>();

        var fallback = new FallbackOrchestrator<TRequest, TRecord>(
            _serviceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger<FallbackOrchestrator<TRequest, TRecord>>());

        for (var i = startIndex; i < partitions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var partition = partitions[i];
            try
            {
                var result = await fallback
                    .ExecuteAsync(partition, adapters, evaluator, cancellationToken)
                    .ConfigureAwait(false);
                var envelope = result.Response;

                await _eventLog.LogAsync(
                    new IngestionEvent(
                        result.SelectedProviderKey.Value,
                        request.DatasetKey,
                        partition.DatasetKey,
                        IngestionAction.Fetched,
                        _clock.UtcNow),
                    cancellationToken).ConfigureAwait(false);

                IReadOnlyList<TRecord> records = envelope.Payload;
                if (normalizer is not null)
                {
                    records = records.Select(normalizer.Normalize).ToList();
                    await _eventLog.LogAsync(
                        new IngestionEvent(
                            result.SelectedProviderKey.Value,
                            request.DatasetKey,
                            partition.DatasetKey,
                            IngestionAction.Normalized,
                            _clock.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                }

                allRecords.AddRange(records);
                allProvenance.AddRange(envelope.Provenance);
                allIssues.AddRange(envelope.Issues);
                adapterCoverages.Add(envelope.Coverage);

                // Advance only over a gap-free success prefix. When failure-continuation is enabled,
                // a later success must not move the checkpoint beyond the missing partition.
                if (isBatch
                    && pipelineId is not null
                    && i == nextContiguousCheckpointIndex)
                {
                    await _checkpointStore.SaveAsync(
                        pipelineId,
                        new IngestionCheckpoint(
                            result.SelectedProviderKey.Value,
                            request.DatasetKey,
                            i,
                            _clock.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                    nextContiguousCheckpointIndex++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Partition fetch failed for {PartitionDatasetKey} in {DatasetKey}.",
                    partition.DatasetKey,
                    request.DatasetKey);

                await _eventLog.LogAsync(
                    new IngestionEvent(
                        "unknown",
                        request.DatasetKey,
                        partition.DatasetKey,
                        IngestionAction.Failed,
                        _clock.UtcNow,
                        "Partition fetch failed; see provider diagnostics for details."),
                    cancellationToken).ConfigureAwait(false);

                allIssues.Add(new DataIssue(
                    IssueCode.PartitionFailed,
                    IssueSeverity.Error,
                    "Partition fetch failed; see provider diagnostics for details."));

                partitionFailed = true;
                // A failed (or fail-stop unattempted) partition has unknown source coverage,
                // never complete coverage. Keep at least one missing point even for sparse
                // requests whose generic calendar expects no observations.
                var missingPartitions = _options.Value.ContinueOnPartitionFailure
                    ? [partition]
                    : partitions.Skip(i);
                foreach (var missingPartition in missingPartitions)
                {
                    var expected = Math.Max(1, _coverageCalculator.Compute(
                        missingPartition.Range, missingPartition.Frequency, 0).RequestedPoints);
                    adapterCoverages.Add(new DataCoverage(expected, 0, expected, 0m));
                }

                if (!_options.Value.ContinueOnPartitionFailure)
                {
                    break;
                }
            }
        }

        // Step 7: Assemble result — prefer adapter-reported coverage (uses source-specific
        // calendars) over pipeline recomputation (uses generic WeekendOnlyCalendar).
        var coverage = adapterCoverages.Count > 0
            ? CoverageComputation.Aggregate(adapterCoverages)
            : _coverageCalculator.Compute(request.Range, request.Frequency, allRecords.Count);

        var finalEnvelope = new DataEnvelope<IReadOnlyList<TRecord>>(
            allRecords.AsReadOnly(),
            coverage,
            allIssues.AsReadOnly(),
            allProvenance.AsReadOnly());

        // Step 8: Failed batches remain retryable in either continuation mode; neither cache
        // tier may turn a transient partition failure into an authoritative whole-request hit.
        var cacheIsAuthoritative = !partitionFailed;

        var isAuthoritativeEmptyDividendWindow = request is DividendHistoryRequest
            && coverage.Status == CompletenessStatus.Complete;
        if (writeThrough is not null
            && (allRecords.Count > 0 || isAuthoritativeEmptyDividendWindow)
            && cacheIsAuthoritative)
        {
            var snapshotEnvelope = new SnapshotEnvelope<IReadOnlyList<TRecord>>(
                s_currentSnapshotSchemaVersion,
                new SnapshotMetadata(_clock.UtcNow, allProvenance.AsReadOnly(), allIssues.AsReadOnly())
                {
                    Coverage = coverage
                },
                allRecords.AsReadOnly());

            await writeThrough
                .WriteAsync(cacheKey, snapshotEnvelope, cancellationToken)
                .ConfigureAwait(false);

            // Step 9: Log Cached event
            await _eventLog.LogAsync(
                new IngestionEvent(
                    allProvenance.FirstOrDefault()?.Provider.Value ?? "unknown",
                    request.DatasetKey,
                    request.DatasetKey,
                    IngestionAction.Cached,
                    _clock.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }

        // Step 10: Promote to L1 only when the result is authoritative. A failed fail-stop run
        // must not poison the cache.
        if (cacheIsAuthoritative)
        {
            await _localCache
                .SetAsync(cacheKey, finalEnvelope, _options.Value.L1CacheTtl, cancellationToken)
                .ConfigureAwait(false);
        }

        return finalEnvelope;
    }

    // Run each single-partition sub-request (produced by BatchRequestSplitter) through the time-series
    // executor and merge the per-partition envelopes into one flat DataEnvelope — the shape the primary
    // ExecuteAsync entry point returns. Each sub-request keys on its own single partition, so the
    // dataset-key builder produces the correct per-partition identity and appends/reads land under
    // distinct keys. Returns null when any sub-request is not time-series-backed (executor returned
    // null), signalling the caller to fall back to the legacy path for the whole request.
    private async Task<DataEnvelope<IReadOnlyList<TRecord>>?> RunPartitionedTimeSeriesAsync<TRequest, TRecord>(
        TimeSeriesPipelineExecutor executor,
        IReadOnlyList<TRequest> partitionRequests,
        CancellationToken cancellationToken)
        where TRequest : IDataRequest
    {
        var payload = new List<TRecord>();
        var provenance = new List<DataProvenance>();
        var issues = new List<DataIssue>();
        var coverages = new List<DataCoverage>();

        foreach (var partitionRequest in partitionRequests)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Per-partition cache key mirrors Step 1's identity so the executor's L1 promotion (and a
            // later single-partition read of the same shape) key on the SAME partition, not the batch.
            var partitionCacheKey = CacheKeyBuilder.FromDateRange(
                $"pipeline-v2/{typeof(TRecord).Name}/{partitionRequest.Frequency}",
                partitionRequest.DatasetKey,
                partitionRequest.Range.From,
                partitionRequest.Range.To);

            DataEnvelope<IReadOnlyList<TRecord>>? envelope;
            try
            {
                envelope = await executor
                    .TryRunAsync<TRequest, TRecord>(partitionRequest, partitionCacheKey, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not (UnsupportedFormatVersionException or CorruptSegmentException))
            {
                _logger.LogError(
                    ex,
                    "Time-series partition fetch failed for {PartitionDatasetKey}.",
                    partitionRequest.DatasetKey);
                issues.Add(new DataIssue(
                    IssueCode.PartitionFailed,
                    IssueSeverity.Error,
                    "Partition fetch failed; see provider diagnostics for details."));
                // A failed partition must contribute one missing logical unit. A zero-requested
                // placeholder is neutral under CoverageComputation.Aggregate and can leave a
                // successful sibling looking Complete, contradicting the PARTITION_FAILED issue.
                coverages.Add(new DataCoverage(1, 0, 1, 0.0m));
                if (!_options.Value.ContinueOnPartitionFailure)
                {
                    break;
                }

                continue;
            }
            if (envelope is null)
            {
                // Not time-series-backed — abandon the store path for the whole request.
                return null;
            }

            payload.AddRange(envelope.Payload);
            provenance.AddRange(envelope.Provenance);
            issues.AddRange(envelope.Issues);
            coverages.Add(envelope.Coverage);
        }

        var coverage = coverages.Count > 0
            ? CoverageComputation.Aggregate(coverages)
            : new DataCoverage(0, 0, 0, 0.0m);

        return new DataEnvelope<IReadOnlyList<TRecord>>(
            payload.AsReadOnly(),
            coverage,
            issues.AsReadOnly(),
            provenance.AsReadOnly());
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<TDatasetKey, DataEnvelope<IReadOnlyList<TRecord>>>>
        ExecutePartitionedAsync<TRequest, TRecord, TPartitionKey, TDatasetKey>(
            TRequest request,
            Func<TRequest, TPartitionKey, TRequest> splitForPartition,
            Func<TRequest, TPartitionKey, TDatasetKey> buildDatasetKey,
            CancellationToken cancellationToken = default)
        where TRequest : IBatchDataRequest<TPartitionKey>
        where TDatasetKey : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(splitForPartition);
        ArgumentNullException.ThrowIfNull(buildDatasetKey);

        // Run each partition through the FULL ExecuteAsync path (L1/L2 cascade, adapter fallback,
        // normalization, coverage) and group the results by the dataset key built for that
        // partition. This is the per-pair FX read: once the pair leaves the
        // FxRate body, the consumer demultiplexes a multi-pair batch by the dataset key (where the
        // pair identity now lives), not by a row field — so the pipeline returns one envelope per
        // dataset key rather than one flat list.
        var partitioned = new Dictionary<TDatasetKey, DataEnvelope<IReadOnlyList<TRecord>>>();

        foreach (var partitionKey in request.PartitionKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var partitionRequest = splitForPartition(request, partitionKey);
            var envelope = await ExecuteAsync<TRequest, TRecord>(partitionRequest, cancellationToken)
                .ConfigureAwait(false);

            var datasetKey = buildDatasetKey(request, partitionKey);
            partitioned[datasetKey] = envelope;
        }

        return partitioned;
    }

    private static string BuildCheckpointProcessorId<TRequest, TRecord>(TRequest request)
        where TRequest : IDataRequest
    {
        var requestType = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        var recordType = typeof(TRecord).FullName ?? typeof(TRecord).Name;
        var datasetKey = request.DatasetKey;

        // Length-prefix variable text so distinct field boundaries cannot produce the same
        // canonical preimage before hashing. Day numbers and frequency complete the request shape.
        var canonicalIdentity = FormattableString.Invariant(
            $"{requestType.Length}:{requestType}{recordType.Length}:{recordType}{datasetKey.Length}:{datasetKey}{request.Range.From.DayNumber}:{request.Range.To.DayNumber}:{(int)request.Frequency}");
        var utf8ByteCount = Encoding.UTF8.GetByteCount(canonicalIdentity);

        if (utf8ByteCount <= MaxStackAllocatedCheckpointIdentityBytes)
        {
            Span<byte> utf8 = stackalloc byte[MaxStackAllocatedCheckpointIdentityBytes];
            var bytesWritten = Encoding.UTF8.GetBytes(canonicalIdentity, utf8);
            return HashCheckpointIdentity(utf8[..bytesWritten]);
        }

        return HashCheckpointIdentity(Encoding.UTF8.GetBytes(canonicalIdentity));
    }

    private static string HashCheckpointIdentity(ReadOnlySpan<byte> identity)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(identity, digest);
        return $"checkpoint.{Convert.ToHexString(digest)}";
    }

    // Reflection fallback for IBatchDataRequest<T> where T is a struct (e.g. DateOnly
    // partition keys). C# generic variance only kicks in for reference-typed T, so a direct
    // `request is IBatchDataRequest<object>` cast misses these cases. This path is only taken
    // when the fast cast already failed.
    private static bool IsBatchRequest<TRequest>(TRequest request)
    {
        return request!.GetType().GetInterfaces().Any(
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBatchDataRequest<>));
    }
}
