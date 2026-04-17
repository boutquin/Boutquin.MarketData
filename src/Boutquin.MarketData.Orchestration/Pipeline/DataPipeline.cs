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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.Keys;
using Boutquin.MarketData.Cache.Policies;
using Boutquin.MarketData.Cache.WriteThrough;
using Boutquin.MarketData.Orchestration.Coverage;
using Boutquin.MarketData.Orchestration.Fallback;
using Boutquin.MarketData.Storage.Checkpointing;
using Boutquin.MarketData.Storage.Events;
using Boutquin.MarketData.Storage.Snapshots;
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
/// persist a partition index after every accepted partition. A retry after process crash resumes at
/// <c>SymbolIndex + 1</c>, skipping partitions already committed to the snapshot store.
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
    public async Task<DataEnvelope<IReadOnlyList<TRecord>>> ExecuteAsync<TRequest, TRecord>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IDataRequest
    {
        // Step 1: Build cache key. Record type + dataset + date range forms the identity so
        // a request for 2026-Q1 does not shadow an earlier request for 2025-Q4 on the same
        // dataset. Including typeof(TRecord).Name also keeps parallel requests for different
        // record shapes on the same dataset (e.g. Bar vs FxRate) in disjoint cache entries.
        var cacheKey = CacheKeyBuilder.FromDateRange(
            typeof(TRecord).Name,
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
                    var snapshotEnvelope = new DataEnvelope<IReadOnlyList<TRecord>>(
                        snapshot.Payload,
                        _coverageCalculator.Compute(request.Range, request.Frequency, snapshot.Payload.Count),
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

        // Step 4: Load checkpoint (batch requests only). Checkpointing lets a partition-heavy
        // batch (e.g. 5000 symbols) survive a mid-run failure by resuming at the next partition
        // instead of re-fetching the already-committed prefix.
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
            pipelineId = $"{typeof(TRequest).Name}:{request.DatasetKey}";
            var checkpoint = await _checkpointStore
                .LoadAsync(pipelineId, cancellationToken)
                .ConfigureAwait(false);
            if (checkpoint is not null)
            {
                startIndex = checkpoint.SymbolIndex + 1;
                _logger.LogInformation("Resuming from index {Index} for {PipelineId}", startIndex, pipelineId);
            }
        }

        // Step 5: Partition
        var partitioner = _serviceProvider.GetService<IRequestPartitioner<TRequest>>();
        var partitions = partitioner?.Partition(request) ?? [request];

        // Step 6: Fetch loop
        var allRecords = new List<TRecord>();
        var allProvenance = new List<DataProvenance>();
        var allIssues = new List<DataIssue>();
        var adapterCoverages = new List<DataCoverage>();
        var partitionFailed = false;

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

                // Save checkpoint after each successful partition
                if (isBatch && pipelineId is not null)
                {
                    await _checkpointStore.SaveAsync(
                        pipelineId,
                        new IngestionCheckpoint(
                            result.SelectedProviderKey.Value,
                            request.DatasetKey,
                            i,
                            _clock.UtcNow),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                await _eventLog.LogAsync(
                    new IngestionEvent(
                        "unknown",
                        request.DatasetKey,
                        partition.DatasetKey,
                        IngestionAction.Failed,
                        _clock.UtcNow,
                        ex.Message),
                    cancellationToken).ConfigureAwait(false);

                allIssues.Add(new DataIssue(
                    IssueCode.PartitionFailed,
                    IssueSeverity.Error,
                    $"Partition {partition.DatasetKey} failed: {ex.Message}"));

                partitionFailed = true;
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

        // Step 8: Write snapshot. Skip persisting when a partition failed under fail-stop mode
        // (ContinueOnPartitionFailure=false). Otherwise a partial batch would be cached and
        // snapshotted as if complete, so the next call would serve the truncated result from
        // L1/L2 without any hint the upstream had errored — the documented fail-stop contract
        // becomes silently-succeed-partial, which is worse than the bug it replaces.
        var cacheIsAuthoritative = !(partitionFailed && !_options.Value.ContinueOnPartitionFailure);

        if (writeThrough is not null && allRecords.Count > 0 && cacheIsAuthoritative)
        {
            var snapshotEnvelope = new SnapshotEnvelope<IReadOnlyList<TRecord>>(
                s_currentSnapshotSchemaVersion,
                new SnapshotMetadata(_clock.UtcNow, allProvenance.AsReadOnly(), allIssues.AsReadOnly()),
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
