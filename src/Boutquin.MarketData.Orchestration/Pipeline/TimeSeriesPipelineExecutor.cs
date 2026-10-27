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

using System.Reflection;

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.Abstractions.Observability;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.Orchestration.Fallback;
using Boutquin.Storage.Domain.Exceptions;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Boutquin.MarketData.Orchestration.Pipeline;

/// <summary>
/// The time-series gap-fill flow that <see cref="DataPipeline.ExecuteAsync{TRequest, TRecord}"/>
/// runs when <see cref="DataPipelineOptions.UseTimeSeriesStore"/> is <see langword="true"/> and a
/// dataset-key builder + live store are registered for the request's record type.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dispatch.</b> <see cref="DataPipeline.ExecuteAsync{TRequest, TRecord}"/> is generic over
/// <c>TRecord</c> with no constraints, but <see cref="ITimeSeriesStoreRegistry.Resolve{TDatasetKey, TRecord}"/>
/// needs a <c>TDatasetKey</c> plus the store constraints. <see cref="TryRunAsync{TRequest, TRecord}"/>
/// bridges the gap: it finds the registered <see cref="IDatasetKeyBuilder{TRequest, TDatasetKey}"/>
/// for the request's runtime type, discovers <c>TDatasetKey</c> from that registration, and invokes
/// the strongly-typed <see cref="RunAsync{TRequest, TRecord, TDatasetKey}"/> worker via reflection —
/// the same runtime-type projection <c>LsmSnapshotReadBinding</c> uses. When no key builder is
/// registered for the request type, it returns <see langword="null"/> so the pipeline falls back to
/// the legacy path.
/// </para>
/// <para>
/// <b>Range merge.</b> The worker resolves a store from the production registry, calls
/// <c>GetCoverageAsync</c>/<c>GetGapsAsync</c>, dispatches adapters for gaps only, <c>AppendAsync</c>s
/// the fetched records, and returns a merged <c>GetRangeAsync</c> read containing both cached
/// and newly fetched observations.
/// </para>
/// </remarks>
internal sealed class TimeSeriesPipelineExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITimeSeriesStoreRegistry _registry;
    private readonly ILocalCache _localCache;
    private readonly IAttemptedRangeIndex _attemptedRanges;
    private readonly DataPipelineOptions _options;
    private readonly IClock _clock;
    private readonly ILogger _logger;

    public TimeSeriesPipelineExecutor(
        IServiceProvider serviceProvider,
        ITimeSeriesStoreRegistry registry,
        ILocalCache localCache,
        IAttemptedRangeIndex attemptedRanges,
        DataPipelineOptions options,
        IClock clock,
        ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _registry = registry;
        _localCache = localCache;
        _attemptedRanges = attemptedRanges;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    // Drop the known-empty already-attempted sub-spans of each gap. The attempted
    // ranges are the inclusive spans the pipeline has already dispatched for this dataset; the source
    // returned everything it had within them, so any date inside them with no stored record is
    // genuinely empty at the source — not a real gap. Subtract the union of attempted ranges from
    // each gap, dispatching ONLY the never-attempted fragments.
    //
    // This is precise per sub-span rather than a single floor/ceiling bounding box: it clamps the
    // pre-inception LEADING span AND a previously-attempted trailing tail (both inside a prior
    // attempted window ⇒ AC-5 stays subsumed), yet it does NOT clamp a suffix or interior span that
    // falls outside every attempted window — e.g. the nightly extend-by-one-day tail whose `To` grows
    // past what was ever attempted, or a never-attempted interior gap between two far-apart
    // attempts. A gap straddling an attempted boundary keeps only its outside fragments.
    private static IReadOnlyList<DateRange> ClampAttemptedSpans(
        IReadOnlyList<DateRange> gaps,
        IReadOnlyList<(DateOnly From, DateOnly To)> attempted)
    {
        if (gaps.Count == 0 || attempted.Count == 0)
        {
            return gaps;
        }

        var result = new List<DateRange>(gaps.Count);
        foreach (var gap in gaps)
        {
            // Sweep the gap left-to-right, emitting the portions not covered by any attempted span.
            // `attempted` is disjoint and ascending by From (maintained by the index's merge).
            var cursor = gap.From;
            var coveredThroughEnd = false;
            foreach (var span in attempted)
            {
                if (span.To < cursor)
                {
                    continue; // attempted span ends before the uncovered cursor — irrelevant
                }

                if (span.From > gap.To)
                {
                    break; // remaining attempted spans start past this gap
                }

                if (span.From > cursor)
                {
                    // Never-attempted fragment before this attempted span.
                    var fragmentTo = span.From.AddDays(-1);
                    result.Add(new DateRange(cursor, fragmentTo < gap.To ? fragmentTo : gap.To));
                }

                if (span.To >= gap.To)
                {
                    coveredThroughEnd = true;
                    break;
                }

                // Advance past the attempted span. The covering-end case above is terminal, so this
                // increment can never run on DateOnly.MaxValue.
                var next = span.To.AddDays(1);
                if (next > cursor)
                {
                    cursor = next;
                }

                if (cursor > gap.To)
                {
                    break;
                }
            }

            if (!coveredThroughEnd && cursor <= gap.To)
            {
                result.Add(new DateRange(cursor, gap.To));
            }
        }

        return result;
    }

    /// <summary>
    /// Runs the time-series gap-fill flow for the request if a dataset-key builder is registered for its runtime
    /// type; otherwise returns <see langword="null"/> to signal "no time-series path — fall back".
    /// </summary>
    public async Task<DataEnvelope<IReadOnlyList<TRecord>>?> TryRunAsync<TRequest, TRecord>(
        TRequest request,
        string cacheKey,
        CancellationToken cancellationToken)
        where TRequest : IDataRequest
    {
        // Find the IDatasetKeyBuilder<TRequest, TDatasetKey> registered for this request type. The
        // open generic is closed over (TRequest, ?) — we scan the registered builder descriptors to
        // discover the concrete TDatasetKey. No builder ⇒ this request type is not time-series-backed.
        var datasetKeyType = ResolveDatasetKeyType(typeof(TRequest));
        if (datasetKeyType is null)
        {
            _logger.LogDebug(
                "No IDatasetKeyBuilder registered for {RequestType}; UseTimeSeriesStore falls back to "
                + "the legacy path for this request.", typeof(TRequest).Name);
            return null;
        }

        var worker = s_runAsyncMethod
            .MakeGenericMethod(typeof(TRequest), typeof(TRecord), datasetKeyType);

        try
        {
            var task = (Task<DataEnvelope<IReadOnlyList<TRecord>>?>)worker.Invoke(
                this, [request, cacheKey, cancellationToken])!;
            return await task.ConfigureAwait(false);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // Unwrap so callers see the real exception (e.g. UnsupportedFormatVersionException,
            // the adapter's exception on a first-gap failure) at the ExecuteAsync call site, not a
            // reflection wrapper.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
            throw; // unreachable
        }
    }

    private static readonly MethodInfo s_runAsyncMethod =
        typeof(TimeSeriesPipelineExecutor).GetMethod(
            nameof(RunAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo s_buildFanOutBundleMethod =
        typeof(TimeSeriesPipelineExecutor).GetMethod(
            nameof(BuildFanOutBundle), BindingFlags.Instance | BindingFlags.NonPublic)!;

    // The strongly-typed worker. TDatasetKey is now statically known, so the registry resolve,
    // coverage/gap reads, and appends are direct calls into the live store.
    private async Task<DataEnvelope<IReadOnlyList<TRecord>>?> RunAsync<TRequest, TRecord, TDatasetKey>(
        TRequest request,
        string cacheKey,
        CancellationToken cancellationToken)
        where TRequest : IDataRequest
        where TRecord : ISerializable<TRecord>, IHasDate, new()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    {
        var store = _registry.Resolve<TDatasetKey, TRecord>();
        if (store is null)
        {
            // Flag on but no store for this record type — fall back to legacy (options doc contract).
            _logger.LogDebug(
                "UseTimeSeriesStore is on but no store is registered for {RecordType}; falling back "
                + "to the legacy path.", typeof(TRecord).Name);
            return null;
        }

        var builder = _serviceProvider
            .GetRequiredService<IDatasetKeyBuilder<TRequest, TDatasetKey>>();
        var selectedProviders = _serviceProvider
            .GetRequiredService<ITimeSeriesSelectedProviderIndex>();
        var recoveryMetadata = _serviceProvider.GetService<ITimeSeriesRecoveryMetadataStore>()
            ?? NoOpTimeSeriesRecoveryMetadataStore.Instance;
        var providerAwareBuilder = builder as IProviderAwareDatasetKeyBuilder<TRequest, TDatasetKey>;
        var recoveredProviders = providerAwareBuilder is null
            ? []
            : await recoveryMetadata
                .GetProvidersAsync(typeof(TRequest), request.DatasetKey, cancellationToken)
                .ConfigureAwait(false);
        ProviderCode knownProvider = default;
        var hasKnownProvider = providerAwareBuilder is not null
            && selectedProviders.TryGet(typeof(TRequest), request.DatasetKey, out knownProvider);
        if (!hasKnownProvider && recoveredProviders.Count > 0)
        {
            // The bounded in-memory index is only an accelerator. Rehydrate the most recently
            // selected durable provider before deciding that a cache gap requires live dispatch.
            knownProvider = recoveredProviders[^1];
            selectedProviders.Record(typeof(TRequest), request.DatasetKey, knownProvider);
            hasKnownProvider = true;
        }

        var datasetKey = providerAwareBuilder is not null && hasKnownProvider
            ? providerAwareBuilder.Build(request, knownProvider)
            : builder.Build(request);
        var contributingKeys = providerAwareBuilder is null
            ? new List<TDatasetKey> { datasetKey }
            : recoveredProviders
                .Select(provider => providerAwareBuilder.Build(request, provider))
                .Distinct()
                .ToList();
        if (!contributingKeys.Contains(datasetKey))
        {
            contributingKeys.Add(datasetKey);
        }

        try
        {
            return await RunStoreFlowAsync<TRequest, TRecord, TDatasetKey>(
                request,
                cacheKey,
                store,
                builder,
                datasetKey,
                contributingKeys,
                selectedProviders,
                recoveryMetadata,
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (UnsupportedFormatVersionException ex)
        {
            // Wire-format version fail-fast: a segment whose tag-0
            // payload declares a format version the reader does not support must fail fast, UNWRAPPED,
            // enriched with the record-type name and the docs anchor so the operator can correlate the
            // break with the migration playbook. The pipeline never silently swallows it.
            throw EnrichFormatVersion<TRecord>(ex);
        }
    }

    // The wire-format-versioning docs anchor surfaced on a fail-fast.
    private const string StorageFormatVersioningDocsAnchor =
        "docs/time-series-cache.md#storage-format-versioning";

    // Re-throw the Storage version exception enriched with the MarketData record-type name and the
    // migration docs anchor, preserving the observed version and chaining the original as the inner
    // exception. Same exception TYPE (unwrapped, per the ACs) — only the message is enriched.
    private static UnsupportedFormatVersionException EnrichFormatVersion<TRecord>(
        UnsupportedFormatVersionException inner)
        => new(
            inner.ObservedVersion,
            $"Time-series store for record type '{typeof(TRecord).Name}' holds a segment whose wire "
            + $"format version ({inner.ObservedVersion}) is newer than this reader supports. This is the "
            + "1.x/2.0 wire break — the store directory was written by an incompatible Boutquin.Storage "
            + $"version. See {StorageFormatVersioningDocsAnchor} for the delete-and-re-ingest migration. "
            + $"Underlying: {inner.Message}",
            inner);

    // The strongly-typed store flow, extracted so the caller can wrap it in the version-enrich
    // catch without duplicating the reflection/dispatch boilerplate above.
    private async Task<DataEnvelope<IReadOnlyList<TRecord>>?> RunStoreFlowAsync<TRequest, TRecord, TDatasetKey>(
        TRequest request,
        string cacheKey,
        ITimeSeriesStore<TDatasetKey, TRecord> store,
        IDatasetKeyBuilder<TRequest, TDatasetKey> builder,
        TDatasetKey datasetKey,
        List<TDatasetKey> contributingKeys,
        ITimeSeriesSelectedProviderIndex selectedProviders,
        ITimeSeriesRecoveryMetadataStore recoveryMetadata,
        CancellationToken cancellationToken)
        where TRequest : IDataRequest
        where TRecord : ISerializable<TRecord>, IHasDate, new()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    {
        // Quarantine authentication can read a shared physical segment while another logical dataset
        // is executing. The complete typed physical key includes provider and every series
        // discriminator (such as price frequency); Range is deliberately excluded so overlapping
        // requests for the same dataset each retain their own bounded recovery range.
        using var quarantineScope = QuarantineRecoveryScope.Enter(
            QuarantineOwnerIdentity<TRequest, TRecord, TDatasetKey>(datasetKey));

        // Per-row-discriminant fan-out:
        // a multi-row-per-date record type fans a fetched list out into per-discriminant keys on append
        // and merges back across a (prefix, *) cross-key scan on read. The keyed-row envelope makes the
        // FETCHED type differ from the STORED type (OptionChainRow → OptionQuote), so the fan-out
        // cannot be probed by the stored TRecord alone; its fetched-row type is DISCOVERED from the
        // fan-out catalogue for this (request, dataset key, stored record) triple — the same
        // reflection-over-registrations shape used to discover TDatasetKey. YieldCurve/Factor are the
        // identity case (TFetched == TStored). A matching fan-out plus a cross-key scan for the record
        // type activates the path; single-row types (Bar/ScalarObservation/FxRate-per-pair) discover no
        // fetched type and take the unchanged path below.
        var fetchedType = ResolveFanOutFetchedType(typeof(TRequest), typeof(TDatasetKey), typeof(TRecord));
        var crossScan = fetchedType is null
            ? null
            : _serviceProvider.GetService<ICrossKeyScan<TDatasetKey, TRecord>>();
        var isFanOut = fetchedType is not null && crossScan is not null;

        // Build the TFetched-ERASED fan-out bundle (prefix-scan bounds + membership filter + the
        // fetch→project→append gap filler) via reflection over the discovered TFetched — mirroring the
        // s_runAsyncMethod dispatch that discovered TDatasetKey. TStored (= TRecord) and TDatasetKey are
        // statically known here; only TFetched is a runtime value, and it is fully hidden inside the
        // bundle's delegates so the rest of this flow stays keyed on TStored/TDatasetKey (the store
        // holds TStored). For the identity case the bundle projects each stored row to its own key.
        FanOutBundle<TRequest, TDatasetKey>? bundle = null;
        if (isFanOut)
        {
            var bundleBuilder = s_buildFanOutBundleMethod
                .MakeGenericMethod(typeof(TRequest), fetchedType!, typeof(TRecord), typeof(TDatasetKey));
            bundle = (FanOutBundle<TRequest, TDatasetKey>)bundleBuilder.Invoke(this, [store, request])!;
        }

        // Step 4: coverage + gaps. Coverage-index / segment read failures surface unwrapped
        // — we do NOT swallow an IOException as "empty".
        // For a fan-out request coverage + gap detection is PER-DISCRIMINANT-KEY, not per-request:
        // a gap in any KNOWN tenor's history is a gap even if other
        // tenors are fully covered. The known tenor keys are the ones the (prefix, *) cross-key scan
        // enumerates (a tenor with no stored row is response-discovered only, healed by
        // a whole-range fetch when the date itself has no coverage). So: enumerate the known keys, take
        // the union of each key's own gaps over the request range, and fetch that union.
        List<TRecord>? fanOutPreScan = null;
        IReadOnlyList<TRecord>? singleRowPreScan = null;
        DateOnly? fanOutNewest = null;
        IReadOnlyList<TDatasetKey> fanOutKnownKeys = [];
        IReadOnlyList<DateRange> rawGaps;
        if (isFanOut)
        {
            var (fanGaps, inRange, newest, knownKeys) = await ComputeFanOutGapsAsync(
                store, crossScan!, bundle!.PrefixBounds, bundle!.KeyMatches, request,
                recoveryMetadata, cancellationToken)
                .ConfigureAwait(false);
            rawGaps = fanGaps;
            fanOutPreScan = inRange;
            fanOutNewest = newest;
            fanOutKnownKeys = knownKeys;
        }
        else
        {
            rawGaps = await ComputeSingleRowGapsAsync(
                store, contributingKeys, request.Range, cancellationToken).ConfigureAwait(false);
            singleRowPreScan = await ReadMergedRangeAsync(store, contributingKeys, request, cancellationToken)
                .ConfigureAwait(false);
        }

        var observedQuarantinedRanges = store is IQuarantineRecoverySource quarantineSource
            ? quarantineSource.DrainQuarantinedRanges()
            : [];
        var quarantinedRanges = new List<DateRange>(observedQuarantinedRanges.Count);
        foreach (var range in observedQuarantinedRanges)
        {
            var from = range.From < request.Range.From ? request.Range.From : range.From;
            var to = range.To > request.Range.To ? request.Range.To : range.To;
            if (from <= to)
            {
                quarantinedRanges.Add(new DateRange(from, to));
            }
        }
        var quarantined = quarantinedRanges.Count > 0;

        // Drop the known-empty already-attempted sub-spans: if the pipeline has
        // already attempted a span for this dataset and the source clipped/returned all it had, any
        // gap sub-span within that attempted span is not a real gap — the source has nothing there.
        // Subtract the attempted spans from each gap so only never-attempted fragments re-dispatch.
        //
        // The attempted-range index is keyed by the request identity (tenor-blind), so its "we already
        // fetched this date span" record is COARSER than a fan-out request's per-discriminant gap.
        // Applying THIS coarse per-request clamp here would suppress a genuine per-tenor gap (AC-F2): a
        // tenor missing an as-of date its siblings cover sits inside a span the curve fetch already
        // attempted, so the clamp would wrongly drop it and the tenor would never heal. So the coarse
        // per-request clamp is bypassed for fan-out (and no per-request attempt is recorded below). The
        // equivalent known-empty suppression IS applied for fan-out, but PER DISCRIMINANT KEY inside
        // ComputeFanOutGapsAsync (rawGaps is already per-key clamped there) — that is the authoritative
        // signal. Single-row types keep the coarse AC-5 clamp verbatim.
        var datasetIdentity = SingleRowDatasetIdentity<TRequest, TRecord, TDatasetKey>(datasetKey);
        var attemptedSpans = isFanOut
            ? []
            : await GetAttemptedRangesAsync(
                datasetIdentity, recoveryMetadata, cancellationToken).ConfigureAwait(false);
        var clampedGaps = isFanOut
            ? rawGaps
            : ClampAttemptedSpans(rawGaps, attemptedSpans);

        // Staleness: if the newest stored record's date is more than the configured max age
        // before "now" (the clock) AND the request reaches into that stale tail, re-fetch the tail.
        // The newest-record probe is GetTailAsync(dataset, 1) — O(tail row group), no calendar guess.
        //
        // The probe key differs by path. A single-row request stores its rows under the one
        // `datasetKey`, so GetTailAsync on it finds the newest. A FAN-OUT request stores its rows only
        // under fully-populated per-discriminant keys (Tenor/FactorName/OptionQuote contract), NEVER
        // under the blanked-discriminant `datasetKey` — so a GetTailAsync(datasetKey, 1) probe would
        // always come back empty and silently disable staleness for every fan-out record type
        // (F-stale-fanout-01). The fan-out path instead reuses the newest as-of date the
        // per-discriminant cross-key scan (ComputeFanOutGapsAsync) already discovered — it visits every
        // stored row of the logical entity across all dates — and applies the same staleness rule.
        var staleAwareGaps = isFanOut
            ? ApplyStalenessToTail(fanOutNewest, request, clampedGaps)
            : await ApplyStalenessAsync(store, datasetKey, request, clampedGaps, cancellationToken)
                .ConfigureAwait(false);

        // Dividend windows are event-sparse. Track fully observed windows separately from empty
        // dates: the former suppress age-based refresh, but never suppress a physical row gap.
        var ordinaryGaps = staleAwareGaps;
        if (!isFanOut && request is DividendHistoryRequest)
        {
            var observed = await recoveryMetadata.GetAttemptedRangesAsync(
                $"observed:{datasetIdentity}", cancellationToken).ConfigureAwait(false);
            // Observation suppresses only age-based refresh. A missing acknowledged row must
            // remain a real gap even when its original source window was observed completely.
            ordinaryGaps = MergeRanges([.. clampedGaps, .. ClampAttemptedSpans(staleAwareGaps, observed)]);
        }
        var gaps = quarantined
            ? MergeRanges([.. ordinaryGaps, .. quarantinedRanges])
            : ordinaryGaps;

        var sourceIdentity = $"{typeof(TRequest).FullName}:{typeof(TRecord).FullName}:{request.Frequency}:{request.DatasetKey}";
        var sourceMetadata = (await recoveryMetadata.GetSourcesAsync(sourceIdentity, request.Range, cancellationToken)
            .ConfigureAwait(false)).ToList();

        if (gaps.Count == 0)
        {
            // Step 5: full subsumption — the cache-hit path. No adapter dispatch. For a fan-out
            // request the pre-scan already materialised the merged curve; reuse it rather than re-scan.
            var cachedRecords = isFanOut
                ? (IReadOnlyList<TRecord>)(fanOutPreScan ?? [])
                : singleRowPreScan ?? [];
            var cacheEnvelope = BuildEnvelope(
                request,
                cachedRecords,
                RetrievalMode.Cache,
                quarantined,
                sourceCoverage: isFanOut ? BuildFanOutCoverage(request.Range, []) : null,
                sourceMetadata: sourceMetadata,
                sourceConfirmedComplete: !isFanOut && rawGaps.Count > 0 && clampedGaps.Count == 0);
            await PromoteAsync(cacheKey, cacheEnvelope, cancellationToken).ConfigureAwait(false);
            return cacheEnvelope;
        }

        // Step 6: gap fill — one adapter dispatch per gap; append the fetched records to the store.
        // The per-gap fetch+append is a TFetched-erased step: a fan-out request uses the bundle's gap
        // filler (fetch TFetched, project each row to its per-discriminant (key, TStored), append per
        // key); a single-row request fetches TRecord and appends the whole list under the one dataset
        // key (unchanged). LSM dedupes on compaction, last-writer-wins; AppendAsync is idempotent
        // read-side.
        var acceptedCoverages = new List<DataCoverage>();
        GapFillMetadata? acceptedMetadata = null;
        Func<TRequest, CancellationToken, Task> gapFill;
        Func<TRequest, bool> hasHandlingAdapter = static _ => true;
        var sourceConfirmedCompleteForGap = false;
        if (isFanOut)
        {
            gapFill = async (gapRequest, ct) =>
            {
                acceptedMetadata = await bundle!.GapFill(gapRequest, ct).ConfigureAwait(false);
                acceptedCoverages.Add(acceptedMetadata.Coverage);
                sourceConfirmedCompleteForGap = acceptedMetadata.Coverage.Status == CompletenessStatus.Complete;
            };
        }
        else
        {
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
            hasHandlingAdapter = gapRequest =>
            {
                try
                {
                    return adapters.Any(adapter => adapter.CanHandle(gapRequest));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A throwing predicate is not evidence that every adapter declined. Let the
                    // fallback boundary audit the failure and try the remaining providers.
                    return true;
                }
            };

            gapFill = async (gapRequest, ct) =>
            {
                var result = await fallback
                    .ExecuteAsync(gapRequest, adapters, evaluator, ct)
                    .ConfigureAwait(false);

                acceptedCoverages.Add(result.Response.Coverage);
                acceptedMetadata = new GapFillMetadata(result.Response.Coverage, result.Response.Issues, result.Response.Provenance);
                sourceConfirmedCompleteForGap = result.Response.Coverage.Status == CompletenessStatus.Complete;

                selectedProviders.Record(typeof(TRequest), request.DatasetKey, result.SelectedProviderKey);
                await recoveryMetadata.RecordProviderAsync(
                    typeof(TRequest), request.DatasetKey, result.SelectedProviderKey, ct).ConfigureAwait(false);
                if (builder is IProviderAwareDatasetKeyBuilder<TRequest, TDatasetKey> providerBuilder)
                {
                    datasetKey = providerBuilder.Build(gapRequest, result.SelectedProviderKey);
                    if (!contributingKeys.Contains(datasetKey))
                    {
                        contributingKeys.Add(datasetKey);
                    }
                }

                IReadOnlyList<TRecord> fetched = result.Response.Payload;
                if (normalizer is not null)
                {
                    fetched = fetched.Select(normalizer.Normalize).ToList();
                }

                await store.AppendAsync(datasetKey, fetched, ct).ConfigureAwait(false);
            };
        }

        var appendedAny = false;
        DateRange? failedRange = null;
        Exception? firstGapException = null;

        for (var i = 0; i < gaps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var gap = gaps[i];
            var gapRequest = (TRequest)request.WithRange(gap);

            try
            {
                // An all-skipped fallback is not a provider failure: it is the ordinary legacy
                // "no registered adapter handles this valid request" outcome. Preserve that
                // issue-bearing empty envelope when the time-series tier is enabled, while real
                // adapter/evaluator failures still flow through the fail-fast branch below.
                if (!hasHandlingAdapter(gapRequest))
                {
                    failedRange = gap;
                    break;
                }

                await gapFill(gapRequest, cancellationToken).ConfigureAwait(false);
                var source = new TimeSeriesSourceMetadata(gap.From, gap.To,
                    acceptedMetadata!.Issues, acceptedMetadata.Provenance);
                await recoveryMetadata.RecordSourceAsync(sourceIdentity, source, cancellationToken).ConfigureAwait(false);
                sourceMetadata.Add(source);
                appendedAny = true;

                // Remember only source-confirmed empty dates, never acknowledged row dates.
                // A later physical gap at a formerly populated date must dispatch again after
                // segment exclusion; an empty-source date can still subsume without another fetch.
                //
                // Single-row: recorded once per request identity. Fan-out: the chain/curve adapter was
                // dispatched over [gap.From, gap.To] for the WHOLE entity, but only the discriminants
                // that CAME BACK EMPTY over that span are permanently source-absent — a rolled-off/
                // expired contract, a discontinued futures month. A discriminant the source DID return
                // (a genuine per-tenor gap the fetch healed) must NOT be recorded: it is covered now, and
                // if it is later re-gapped it must re-dispatch, not be clamped (AC-F2). So re-probe each
                // known key's gaps within the just-filled span and record only the still-missing
                // sub-spans per fully-populated discriminant key; the next read clamps exactly those.
                // The coarse per-request identity stays unrecorded for fan-out (tenor-blind, AC-F2).
                if (isFanOut && sourceConfirmedCompleteForGap)
                {
                    foreach (var key in fanOutKnownKeys)
                    {
                        var stillMissing = await store.GetGapsAsync(key, gap, cancellationToken)
                            .ConfigureAwait(false);
                        foreach (var span in stillMissing)
                        {
                            var physicalIdentity = FanOutKeyIdentity(key);
                            _attemptedRanges.RecordAttempt(physicalIdentity, span.From, span.To);
                            await recoveryMetadata.RecordAttemptAsync(
                                physicalIdentity, span.From, span.To, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
                else if (!isFanOut)
                {
                    // A populated Partial response is unresolved coverage, not a source-confirmed
                    // empty span. Recording it would turn a later cache replay into Complete solely
                    // by clamping the original gap (C-038).
                    if (sourceConfirmedCompleteForGap)
                    {
                        var physicalIdentity = SingleRowDatasetIdentity<TRequest, TRecord, TDatasetKey>(datasetKey);
                        var stillMissing = await store.GetGapsAsync(datasetKey, gap, cancellationToken)
                            .ConfigureAwait(false);
                        foreach (var span in stillMissing)
                        {
                            _attemptedRanges.RecordAttempt(physicalIdentity, span.From, span.To);
                            await recoveryMetadata.RecordAttemptAsync(
                                physicalIdentity, span.From, span.To, cancellationToken).ConfigureAwait(false);
                        }

                        if (request is DividendHistoryRequest)
                        {
                            await recoveryMetadata.RecordAttemptAsync(
                                $"observed:{physicalIdentity}", gap.From, gap.To, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Gap-fill policy: a gap-fill failure is surfaced, never swallowed. If the FIRST
                // gap fails and nothing was appended, rethrow the adapter exception (no Cache/Live/
                // Partial masking an empty fetch). If a later gap fails after an earlier append, the
                // durable appends are retained and the envelope is Partial with the failing range.
                if (!appendedAny)
                {
                    firstGapException = ex;
                }

                failedRange = gap;
                break;
            }
        }

        if (firstGapException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(firstGapException);
        }

        // Step 7: merged range read from the store (now includes the just-appended gap records). A
        // fan-out request merges across the per-discriminant keys via the (prefix, *) cross-key scan,
        // returned in dataset-key order; a single-row request reconciles all contributing providers.
        var merged = isFanOut
            ? await ScanFanOutCurveAsync(
                crossScan!, bundle!.PrefixBounds, bundle!.KeyMatches, request,
                request.Range.From, request.Range.To, cancellationToken)
                .ConfigureAwait(false)
            : await ReadMergedRangeAsync(store, contributingKeys, request, cancellationToken)
                .ConfigureAwait(false);

        // The merged read still visits the damaged segment until compaction retires it. Drain the
        // recovery range it requeued so this completed call cannot leak work into the next one;
        // a later request authenticates the segment again and requeues it if damage remains.
        _ = (store as IQuarantineRecoverySource)?.DrainQuarantinedRanges();

        // Step 8: provenance. Full-range gap fill (the whole request was a gap and it all succeeded)
        // is Live; a mix of pre-existing coverage and fresh fetch — or a partially-failed fill — is
        // Partial.
        var wholeRangeFetched = failedRange is null && GapsCoverWholeRange(gaps, request.Range);
        var mode = quarantined || !wholeRangeFetched
            ? RetrievalMode.Partial
            : RetrievalMode.Live;
        var sourceCoverage = wholeRangeFetched && acceptedCoverages.Count > 0
            ? CoverageComputation.Aggregate(acceptedCoverages)
            : null;
        if (isFanOut && sourceCoverage is null)
        {
            // Count dates with complete known-discriminant coverage, not physical rows.
            // One populated factor cannot compensate for a different factor's missing date.
            var remaining = await ComputeFanOutGapsAsync(
                store, crossScan!, bundle!.PrefixBounds, bundle.KeyMatches, request,
                recoveryMetadata, cancellationToken).ConfigureAwait(false);
            var unresolved = failedRange is { } failed
                ? MergeRanges([.. remaining.Gaps, failed])
                : remaining.Gaps;
            sourceCoverage = BuildFanOutCoverage(request.Range, unresolved);
        }

        var envelope = BuildEnvelope(
            request,
            merged,
            mode,
            quarantined,
            failedRange,
            sourceCoverage,
            sourceMetadata,
            sourceConfirmedComplete: request is DividendHistoryRequest
                && failedRange is null && !quarantined && acceptedCoverages.Count > 0
                && acceptedCoverages.All(static coverage => coverage.Status == CompletenessStatus.Complete));

        // Step 9: promote to L1 and return — but ONLY when the fill completed. A partially-failed
        // fill (failedRange set) is known-incomplete; promoting it would let the next identical call
        // hit L1 and be served as Cache over the unfilled range, so the failed gap would never be
        // re-attempted for the whole L1 TTL — violating AC-25 ("never Cache over an incompletely-
        // filled range") and defeating the store tier's self-healing. This mirrors the legacy path's
        // `cacheIsAuthoritative` guard (DataPipeline.cs). A successful Partial (coverage + live mix,
        // failedRange null) is authoritative and still promotes.
        if (failedRange is null)
        {
            await PromoteAsync(cacheKey, envelope, cancellationToken).ConfigureAwait(false);
        }

        return envelope;
    }

    // Staleness: probe the newest stored record via GetTailAsync(dataset, 1). If its date is
    // more than TimeSeriesStalenessMaxAge before the clock's "now" AND the request extends to/past
    // that date, add a trailing gap (newestDate .. request.To) so the stale tail re-fetches.
    // Merges with any existing suffix gap rather than double-fetching.
    private async Task<IReadOnlyList<DateRange>> ApplyStalenessAsync<TRecord, TDatasetKey>(
        ITimeSeriesStore<TDatasetKey, TRecord> store,
        TDatasetKey datasetKey,
        IDataRequest request,
        IReadOnlyList<DateRange> gaps,
        CancellationToken cancellationToken)
        where TRecord : ISerializable<TRecord>, IHasDate, new()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    {
        if (_options.TimeSeriesStalenessMaxAge is null)
        {
            // Staleness disabled — skip the tail probe entirely.
            return gaps;
        }

        DateOnly? newest = null;
        await foreach (var record in store.GetTailAsync(datasetKey, 1, onOrBefore: null, projection: null, cancellationToken)
            .ConfigureAwait(false))
        {
            newest = record.Date;
            break;
        }

        return ApplyStalenessToTail(newest, request, gaps);
    }

    // The staleness rule proper, factored out of the single-row tail probe so BOTH paths share one
    // implementation: the single-row path reads the newest date via GetTailAsync on its one dataset
    // key, and the fan-out path supplies the newest as-of already discovered across the
    // per-discriminant keys by the cross-key scan (blanked-key probe would find nothing —
    // F-stale-fanout-01). Given the newest stored date, if it is more than TimeSeriesStalenessMaxAge
    // before the clock's "now" AND the request extends to/past that date, add a trailing gap
    // (newest .. request.To) so the stale tail re-fetches, merged with any adjacent/overlapping gap.
    private IReadOnlyList<DateRange> ApplyStalenessToTail(
        DateOnly? newest,
        IDataRequest request,
        IReadOnlyList<DateRange> gaps)
    {
        var maxAge = _options.TimeSeriesStalenessMaxAge;
        if (maxAge is not { } age)
        {
            return gaps;
        }

        if (newest is not { } newestDate)
        {
            // No stored records — the whole request is already a gap; staleness adds nothing.
            return gaps;
        }

        var nowDate = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var ageInDays = nowDate.DayNumber - newestDate.DayNumber;
        if (ageInDays <= age.TotalDays || request.Range.To < newestDate)
        {
            // The newest record is fresh enough, or the request does not reach the stale tail.
            return gaps;
        }

        // Stale tail: re-fetch from the newest stored record (inclusive — the stale record itself
        // is refreshed) through request.To. This is distinct from a plain suffix gap: it fires even
        // when the store's date coverage reaches request.To, because the newest record's age (vs the
        // clock) — not date coverage — is the trigger. Merge with any adjacent/overlapping gap.
        var staleFrom = newestDate < request.Range.From ? request.Range.From : newestDate;
        var staleGap = new DateRange(staleFrom, request.Range.To);

        return MergeRanges([.. gaps, staleGap]);
    }

    private static async Task<IReadOnlyList<DateRange>> ComputeSingleRowGapsAsync<TRecord, TDatasetKey>(
        ITimeSeriesStore<TDatasetKey, TRecord> store,
        IReadOnlyList<TDatasetKey> contributingKeys,
        DateRange requestedRange,
        CancellationToken cancellationToken)
        where TRecord : ISerializable<TRecord>, IHasDate, new()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    {
        var physicalGaps = new List<IReadOnlyList<DateRange>>(contributingKeys.Count);
        foreach (var key in contributingKeys)
        {
            _ = await store.GetCoverageAsync(key, cancellationToken).ConfigureAwait(false);
            physicalGaps.Add(await store.GetGapsAsync(key, requestedRange, cancellationToken)
                .ConfigureAwait(false));
        }

        // A logical date is missing only when every contributing provider key lacks it. This lets a
        // fallback provider fill a tail without causing the earlier provider's prefix to re-dispatch.
        return IntersectRanges(physicalGaps);
    }

    private static IReadOnlyList<DateRange> IntersectRanges(
        IReadOnlyList<IReadOnlyList<DateRange>> rangeSets)
    {
        if (rangeSets.Count == 0)
        {
            return [];
        }

        var intersection = rangeSets[0]
            .OrderBy(static range => range.From)
            .ToList();
        for (var setIndex = 1; setIndex < rangeSets.Count && intersection.Count > 0; setIndex++)
        {
            var next = rangeSets[setIndex]
                .OrderBy(static range => range.From)
                .ToList();
            var narrowed = new List<DateRange>();
            var left = 0;
            var right = 0;
            while (left < intersection.Count && right < next.Count)
            {
                var first = intersection[left];
                var second = next[right];
                var from = first.From > second.From ? first.From : second.From;
                var to = first.To < second.To ? first.To : second.To;
                if (from <= to)
                {
                    narrowed.Add(new DateRange(from, to));
                }

                if (first.To < second.To)
                {
                    left++;
                }
                else if (second.To < first.To)
                {
                    right++;
                }
                else
                {
                    left++;
                    right++;
                }
            }

            intersection = narrowed;
        }

        return intersection;
    }

    private async Task<IReadOnlyList<(DateOnly From, DateOnly To)>> GetAttemptedRangesAsync(
        string datasetIdentity,
        ITimeSeriesRecoveryMetadataStore recoveryMetadata,
        CancellationToken cancellationToken)
    {
        var accelerated = _attemptedRanges.GetAttemptedRanges(datasetIdentity);
        if (accelerated.Count > 0)
        {
            return accelerated;
        }

        var durable = await recoveryMetadata
            .GetAttemptedRangesAsync(datasetIdentity, cancellationToken).ConfigureAwait(false);
        foreach (var span in durable)
        {
            _attemptedRanges.RecordAttempt(datasetIdentity, span.From, span.To);
        }

        return durable;
    }

    private static async Task<IReadOnlyList<TRecord>> ReadMergedRangeAsync<TRecord, TDatasetKey>(
        ITimeSeriesStore<TDatasetKey, TRecord> store,
        IReadOnlyList<TDatasetKey> contributingKeys,
        IDataRequest request,
        CancellationToken cancellationToken)
        where TRecord : ISerializable<TRecord>, IHasDate, new()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    {
        // Provider conflict policy: order physical keys by their canonical key text and keep the
        // first record for a date. It is deterministic across restart and independent of fallback
        // response timing; later provider rows fill dates absent from earlier keys.
        var byDate = new SortedDictionary<DateOnly, TRecord>();
        foreach (var key in contributingKeys
            .OrderBy(static key => key.ToString() ?? string.Empty, StringComparer.Ordinal))
        {
            await foreach (var record in store.GetRangeAsync(
                key, request.Range.From, request.Range.To, projection: null, cancellationToken)
                .ConfigureAwait(false))
            {
                byDate.TryAdd(record.Date, record);
            }
        }

        return byDate.Values.ToList();
    }

    // Cross-key read merge: scan the (prefix, *) range for the request's logical entity and
    // return the covered records in dataset-key order (dataset ascending, then date). The engine's
    // composite-key range bounds only the boundary datasets by date, so records outside [from, to] and
    // any over-scan into an adjacent entity (the prefix upper bound is a generous sentinel) are dropped
    // by the date window and the fan-out's own membership predicate. The result is already in key order.
    private static async Task<List<TRecord>> ScanFanOutCurveAsync<TRequest, TRecord, TDatasetKey>(
        ICrossKeyScan<TDatasetKey, TRecord> scan,
        Func<TRequest, (TDatasetKey Lo, TDatasetKey Hi)> prefixBounds,
        Func<TRequest, TDatasetKey, bool> keyMatches,
        TRequest request,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
        where TRequest : IDataRequest
        where TRecord : ISerializable<TRecord>, IHasDate, new()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    {
        var (lo, hi) = prefixBounds(request);
        var records = new List<TRecord>();
        await foreach (var (dataset, record) in scan.ScanAsync(lo, from, hi, to, cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.Date < from || record.Date > to)
            {
                continue;
            }

            if (!keyMatches(request, dataset))
            {
                continue;
            }

            records.Add(record);
        }

        return records;
    }

    // Per-discriminant-key coverage + gap detection. A single
    // (prefix, *) cross-key scan over the whole date range does double duty: it enumerates the KNOWN
    // discriminant keys (each dataset that has at least one stored row) AND materialises the records
    // inside the request range for the potential cache-hit read. Gap detection is then per key: the
    // union of every known key's own gaps over the request range. A key with a hole in the range
    // (e.g. one tenor missing an as-of date the others cover) contributes that hole to the union, so
    // it is healed without forcing a re-fetch of the tenors that are already covered. When no key is
    // known the whole request range is a gap (a cold curve — the fetch discovers the tenor set and the
    // fan-out append heals it idempotently). Each known key's gaps are additionally
    // clamped by that key's OWN attempted ranges (per-discriminant AC-5, keyed by the fully-populated
    // discriminant key) so a permanently source-absent contract's hole is dispatched once, not on every
    // read; the known keys are returned so the fill loop can record the per-key attempt for whichever
    // came back empty.
    private async Task<(IReadOnlyList<DateRange> Gaps, List<TRecord> InRange, DateOnly? Newest, IReadOnlyList<TDatasetKey> KnownKeys)>
        ComputeFanOutGapsAsync<TRequest, TRecord, TDatasetKey>(
            ITimeSeriesStore<TDatasetKey, TRecord> store,
            ICrossKeyScan<TDatasetKey, TRecord> scan,
            Func<TRequest, (TDatasetKey Lo, TDatasetKey Hi)> prefixBounds,
            Func<TRequest, TDatasetKey, bool> keyMatches,
            TRequest request,
            ITimeSeriesRecoveryMetadataStore recoveryMetadata,
            CancellationToken cancellationToken)
        where TRequest : IDataRequest
        where TRecord : ISerializable<TRecord>, IHasDate, new()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    {
        var (lo, hi) = prefixBounds(request);
        var from = request.Range.From;
        var to = request.Range.To;

        var knownKeys = new List<TDatasetKey>();
        var seen = new HashSet<TDatasetKey>();
        var inRange = new List<TRecord>();

        // The newest as-of date across EVERY stored row of the logical entity (all discriminant keys,
        // all dates). This is the fan-out equivalent of the single-row GetTailAsync(dataset, 1) probe:
        // the entity's tail is stale iff its freshest as-of is older than the staleness window, and
        // the tail re-fetch heals the whole curve/chain at once. The blanked-discriminant `datasetKey`
        // holds no rows, so this scan — not a tail probe on that key — is the only correct source
        // (F-stale-fanout-01).
        DateOnly? newest = null;

        // Enumerate the whole prefix (all dates) so a discriminant known only from OTHER dates is still
        // a known key whose gap in the request range is detectable.
        await foreach (var (dataset, record) in scan
            .ScanAsync(lo, DateOnly.MinValue, hi, DateOnly.MaxValue, cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!keyMatches(request, dataset))
            {
                continue;
            }

            if (seen.Add(dataset))
            {
                knownKeys.Add(dataset);
            }

            if (newest is not { } n || record.Date > n)
            {
                newest = record.Date;
            }

            if (record.Date >= from && record.Date <= to)
            {
                inRange.Add(record);
            }
        }

        if (knownKeys.Count == 0)
        {
            // Cold curve — the whole range is a gap; the fetch discovers the tenor set. No stored rows,
            // so `newest` is null and staleness adds nothing on top of the whole-range gap, and there is
            // no known key to clamp or record an attempt against (discovered keys heal via fresh rows).
            return ([request.Range], inRange, newest, []);
        }

        var union = new List<DateRange>();
        foreach (var key in knownKeys)
        {
            var keyGaps = await store.GetGapsAsync(key, request.Range, cancellationToken)
                .ConfigureAwait(false);

            // Per-discriminant AC-5 clamp: subtract this discriminant's already-attempted-and-empty
            // spans. A discriminant that is historically known (>=1 stored row) but permanently
            // source-absent over part of the range — a rolled-off/expired option contract, a
            // discontinued futures month — otherwise re-contributes its permanent hole to the union on
            // EVERY read and re-dispatches the adapter forever. Once the chain/curve fetch over that span
            // came back without the contract, the still-missing span is recorded as attempted for THIS
            // key (after a successful fill, in RunStoreFlowAsync) and clamped here so it never re-
            // dispatches — the fan-out analogue of the single-row per-request clamp (which stays bypassed
            // for fan-out because it is tenor-blind, AC-F2).
            var physicalIdentity = FanOutKeyIdentity(key);
            var attempted = await GetAttemptedRangesAsync(
                physicalIdentity, recoveryMetadata, cancellationToken).ConfigureAwait(false);
            var clamped = ClampAttemptedSpans(keyGaps, attempted);
            union.AddRange(clamped);
        }

        return (MergeRanges(union), inRange, newest, knownKeys);
    }

    // Per-discriminant attempted-range identity (AC-5 for fan-out). The coarse per-request attempted-
    // range index is tenor-blind, so fan-out attempts are tracked per fully-populated discriminant key
    // — a property of the physical series, shared by any request touching it. The key's canonical form
    // already identifies the series uniquely (it is the store partition key); a fixed "fanout:" prefix
    // namespaces it away from the single-key path's per-request identities in the shared index so the
    // two can never collide.
    private static string FanOutKeyIdentity<TDatasetKey>(TDatasetKey key)
        => $"fanout:{key}";

    // The v2 identity excludes unsafe historical full-window claims from empty-date recovery.
    // Single-row attempted ranges are scoped to the physical provider key. Different request/record
    // families can share key text, and an attempted empty range from provider A must never suppress
    // a later request to provider B, so both closed types and the complete physical key participate.
    private static string SingleRowDatasetIdentity<TRequest, TRecord, TDatasetKey>(TDatasetKey datasetKey)
        => $"single-empty-v2:{typeof(TRequest).FullName}:{typeof(TRecord).FullName}:{datasetKey}";

    // Quarantine ownership must partition by the same complete physical key used by the store,
    // including provider and every discriminator. Unlike cache keys, it intentionally omits Range:
    // independent overlapping requests for one physical dataset both need to recover their own span.
    private static string QuarantineOwnerIdentity<TRequest, TRecord, TDatasetKey>(TDatasetKey datasetKey)
        => $"quarantine:{typeof(TRequest).FullName}:{typeof(TRecord).FullName}:{datasetKey}";

    // Coalesce a set of (possibly overlapping/duplicate/adjacent) date ranges into the minimal
    // ascending set — the per-key gaps of a fan-out request overlap heavily (every uncovered tenor
    // contributes the same missing span), so the union must dedup before dispatch.
    private static IReadOnlyList<DateRange> MergeRanges(List<DateRange> ranges)
    {
        if (ranges.Count <= 1)
        {
            return ranges;
        }

        ranges.Sort(static (a, b) => a.From.CompareTo(b.From));
        var merged = new List<DateRange>(ranges.Count);
        var current = ranges[0];
        for (var i = 1; i < ranges.Count; i++)
        {
            var next = ranges[i];
            if (next.From.DayNumber <= current.To.DayNumber + 1)
            {
                // Overlapping or adjacent — widen the current span.
                var to = next.To > current.To ? next.To : current.To;
                current = new DateRange(current.From, to);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    // Append fan-out: project each FETCHED row to its fully-populated per-discriminant key
    // and STORED record, then AppendAsync the co-keyed stored records per key. The keyed-row envelope
    // makes TFetched (e.g. OptionChainRow) distinct from TStored (e.g. OptionQuote); the identity
    // (body-discriminant) case is simply TFetched == TStored. Two fetched rows that project to the SAME
    // (key, date) are a residual collision: the store keeps the last writer, and a DataQualityIncident
    // (SPEC-OBS-01) names the collapsed (DatasetKey, Date) so it is observable rather than silent.
    //
    // Normalization is resolved and applied here, on TStored, AFTER Project — never before it, and
    // never on TFetched. Two reasons: (1) TStored is the type a registered normalizer can actually
    // exist for (OptionQuoteNormalizer is typed on OptionQuote, the stored type, not on the fetched
    // OptionChainRow — resolving on TFetched can never find it for a fetched≠stored fan-out); (2) the
    // dataset key is derived by Project from the fetched row's own discriminant columns
    // (OptionChainRowFanOut reads row.Contract; FactorObservationFanOut/YieldCurveQuoteFanOut
    // canonicalise independently of normalization state), so normalizing strictly after Project can
    // never influence which key a record lands under.
    private async Task AppendFanOutStoredAsync<TRequest, TFetched, TStored, TDatasetKey>(
        ITimeSeriesStore<TDatasetKey, TStored> store,
        IDatasetKeyFanOut<TRequest, TDatasetKey, TFetched, TStored> fanOut,
        TRequest request,
        IReadOnlyList<TFetched> fetched,
        INormalizer<TStored, TStored>? normalizer,
        CancellationToken cancellationToken)
        where TRequest : IDataRequest
        where TStored : ISerializable<TStored>, IHasDate, new()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    {
        if (fetched.Count == 0)
        {
            return;
        }

        var groups = new Dictionary<TDatasetKey, List<TStored>>();
        var order = new List<TDatasetKey>();
        foreach (var row in fetched)
        {
            var (key, record) = fanOut.Project(request, row);
            if (normalizer is not null)
            {
                record = normalizer.Normalize(record);
            }

            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
                order.Add(key);
            }

            list.Add(record);
        }

        foreach (var key in order)
        {
            var list = groups[key];

            // A (key, date) appearing more than once collapses last-writer-wins on the store's
            // idempotent upsert — surface each such collapse as a diagnostic before the append.
            var perDate = new Dictionary<DateOnly, int>();
            foreach (var record in list)
            {
                perDate[record.Date] = perDate.TryGetValue(record.Date, out var n) ? n + 1 : 1;
            }

            foreach (var (date, count) in perDate)
            {
                if (count > 1)
                {
                    EmitCollapseIncident<TStored, TDatasetKey>(key, date, count);
                }
            }

            await store.AppendAsync(key, list, cancellationToken).ConfigureAwait(false);
        }
    }

    // The TFetched-ERASED fan-out bundle: the prefix-scan bounds and membership filter (both
    // TFetched/TStored-free in signature) plus the fetch→project→append gap filler (which fetches
    // TFetched and appends TStored). Constructing these three delegates inside a TFetched-generic
    // factory (BuildFanOutBundle) and exposing only this TFetched-free record is what lets the store
    // flow stay keyed on TStored/TDatasetKey while the adapter dispatch + Project run on TFetched —
    // the fetched-differs-from-stored generalization, reached by reflection over the discovered
    // TFetched (mirroring the s_runAsyncMethod discovery of TDatasetKey).
    private sealed class FanOutBundle<TRequest, TDatasetKey>(
        Func<TRequest, (TDatasetKey Lo, TDatasetKey Hi)> prefixBounds,
        Func<TRequest, TDatasetKey, bool> keyMatches,
        Func<TRequest, CancellationToken, Task<GapFillMetadata>> gapFill)
    {
        public Func<TRequest, (TDatasetKey Lo, TDatasetKey Hi)> PrefixBounds { get; } = prefixBounds;

        public Func<TRequest, TDatasetKey, bool> KeyMatches { get; } = keyMatches;

        public Func<TRequest, CancellationToken, Task<GapFillMetadata>> GapFill { get; } = gapFill;
    }

    private sealed record GapFillMetadata(
        DataCoverage Coverage, IReadOnlyList<DataIssue> Issues, IReadOnlyList<DataProvenance> Provenance);

    // Builds the TFetched-erased fan-out bundle for a (request, dataset key, stored record) whose
    // discovered fetched-row type is TFetched. Resolves the concrete
    // IDatasetKeyFanOut<TRequest, TDatasetKey, TFetched, TStored> (for the identity case TFetched ==
    // TStored) plus the TFetched-typed adapter/evaluator/normalizer/fallback, and closes over them in
    // three delegates whose signatures never mention TFetched. Invoked by reflection from
    // RunStoreFlowAsync via s_buildFanOutBundleMethod.
    private FanOutBundle<TRequest, TDatasetKey> BuildFanOutBundle<TRequest, TFetched, TStored, TDatasetKey>(
        ITimeSeriesStore<TDatasetKey, TStored> store,
        TRequest request)
        where TRequest : IDataRequest
        where TStored : ISerializable<TStored>, IHasDate, new()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    {
        var fanOut = _serviceProvider
            .GetRequiredService<IDatasetKeyFanOut<TRequest, TDatasetKey, TFetched, TStored>>();

        var adapters = _serviceProvider
            .GetServices<IDataSourceAdapter<TRequest, TFetched>>()
            .ToList();
        var evaluator = _serviceProvider
            .GetService<IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<TFetched>>>>()
            ?? new CoverageResponseEvaluator<TFetched>();
        // Resolved on TStored — see the rationale on AppendFanOutStoredAsync above. For the identity
        // fan-outs (FactorObservation, YieldCurveQuote) TFetched == TStored, so this is the same
        // registration a TFetched-keyed resolve would have found; for the keyed-row envelope
        // (OptionChainRow → OptionQuote) it is the ONLY resolution that can ever find
        // OptionQuoteNormalizer, which is registered as INormalizer<OptionQuote, OptionQuote>.
        var normalizer = _serviceProvider.GetService<INormalizer<TStored, TStored>>();
        var fallback = new FallbackOrchestrator<TRequest, TFetched>(
            _serviceProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger<FallbackOrchestrator<TRequest, TFetched>>());

        // The full request supplies the key prefix columns to Project; the per-gap request narrows the
        // fetch range only (its prefix columns are identical after WithRange). This matches the
        // pre-generalization behaviour, which passed the full request to the append projection.
        async Task<GapFillMetadata> GapFillAsync(TRequest gapRequest, CancellationToken ct)
        {
            var result = await fallback
                .ExecuteAsync(gapRequest, adapters, evaluator, ct)
                .ConfigureAwait(false);

            IReadOnlyList<TFetched> fetched = result.Response.Payload;
            await AppendFanOutStoredAsync(store, fanOut, request, fetched, normalizer, ct)
                .ConfigureAwait(false);
            return new GapFillMetadata(result.Response.Coverage, result.Response.Issues, result.Response.Provenance);
        }

        return new FanOutBundle<TRequest, TDatasetKey>(
            fanOut.PrefixBounds,
            fanOut.KeyMatchesRequest,
            GapFillAsync);
    }

    // Emit the SPEC-OBS-01 collapse diagnostic to the configured incident sink, or the logger when
    // none is registered. Never throws — an observability failure must not abort the gap fill.
    private void EmitCollapseIncident<TRecord, TDatasetKey>(TDatasetKey key, DateOnly date, int count)
    {
        var incident = new DataQualityIncident(
            DataQualityIncidentKind.IngestionAnomaly,
            typeof(TRecord).Name,
            key?.ToString(),
            _clock.UtcNow,
            $"Per-discriminant fan-out collapsed {count} co-dated rows into 1 for dataset '{key}' at "
            + $"{date:O} (last-writer-wins). A residual (DatasetKey, Date) collision remains after "
            + "fan-out — the discriminant does not uniquely identify the row.");

        var sink = _serviceProvider.GetService<IDataQualityIncidentSink>()
            ?? _options.DataQualityIncidentSink;
        if (sink is not null)
        {
            sink.Report(incident);
            return;
        }

        _logger.LogWarning(
            "Data-quality incident {Kind} on {RecordType} (dataset {DatasetKey}) at {OccurredAtUtc:o}: {Detail}",
            incident.Kind,
            incident.RecordType,
            incident.DatasetKey ?? "<none>",
            incident.OccurredAtUtc,
            incident.Detail);
    }

    private async Task PromoteAsync<TRecord>(
        string cacheKey,
        DataEnvelope<IReadOnlyList<TRecord>> envelope,
        CancellationToken cancellationToken)
        => await _localCache
            .SetAsync(cacheKey, envelope, _options.L1CacheTtl, cancellationToken)
            .ConfigureAwait(false);

    // Whether the returned gaps span the entire requested range (no pre-existing coverage). Two
    // gaps back-to-back covering [From, To] with nothing in between means the store had no
    // overlapping coverage — a full live fetch. Any covered interior span makes the read Partial.
    private static bool GapsCoverWholeRange(IReadOnlyList<DateRange> gaps, DateRange requested)
    {
        var coveredDays = gaps.Sum(g => g.To.DayNumber - g.From.DayNumber + 1);
        var requestedDays = requested.To.DayNumber - requested.From.DayNumber + 1;
        return coveredDays >= requestedDays;
    }

    private static DataCoverage BuildFanOutCoverage(DateRange requested, IReadOnlyList<DateRange> unresolved)
    {
        var requestedDays = requested.To.DayNumber - requested.From.DayNumber + 1;
        var missingDays = unresolved.Sum(static gap => gap.To.DayNumber - gap.From.DayNumber + 1);
        var coveredDays = requestedDays - missingDays;
        return new DataCoverage(requestedDays, coveredDays, missingDays, (decimal)coveredDays / requestedDays);
    }

    private DataEnvelope<IReadOnlyList<TRecord>> BuildEnvelope<TRecord>(
        IDataRequest request,
        IReadOnlyList<TRecord> records,
        RetrievalMode mode,
        bool quarantined,
        DateRange? failingRange = null,
        DataCoverage? sourceCoverage = null,
        IReadOnlyList<TimeSeriesSourceMetadata>? sourceMetadata = null,
        bool sourceConfirmedComplete = false)
        where TRecord : IHasDate
    {
        var returned = records.Count;
        var requestedDays = request.Range.To.DayNumber - request.Range.From.DayNumber + 1;
        var coverage = sourceCoverage
            ?? (sourceConfirmedComplete
                ? new DataCoverage(returned, returned, 0, 1.0m)
                : new DataCoverage(
                    RequestedPoints: requestedDays,
                    ReturnedPoints: returned,
                    MissingPoints: Math.Max(0, requestedDays - returned),
                    CoverageRatio: requestedDays > 0
                        ? Math.Min(1.0m, (decimal)returned / requestedDays)
                        : 0.0m));

        var sources = sourceMetadata ?? [];
        var provenance = sources.SelectMany(static source => source.Provenance)
            .Select(p => p with
            {
                RetrievalMode = mode,
                SourceUrl = mode == RetrievalMode.Cache ? null : p.SourceUrl,
            }).Distinct().ToList();
        if (provenance.Count == 0 || records.Any(record => !sources.Any(source =>
            source.From <= record.Date && source.To >= record.Date && source.Provenance.Count > 0)))
        {
            provenance.Add(new DataProvenance(
                    Provider: new ProviderCode("timeseries"),
                    Dataset: request.DatasetKey,
                    LicenseFlag: LicenseType.Unknown,
                    RetrievalMode: mode,
                    Freshness: FreshnessClass.EndOfDay,
                    RetrievedAtUtc: _clock.UtcNow,
                    SourceUrl: null));
        }

        var issues = sources.SelectMany(static source => source.Issues).Distinct().ToList();
        if (failingRange is { } fr)
        {
            issues.Add(new DataIssue(
                IssueCode.PartitionFailed,
                IssueSeverity.Error,
                $"Gap fill did not complete: range {fr.From:O}..{fr.To:O} remains unfilled."));
        }

        if (quarantined)
        {
            issues.Add(new DataIssue(
                IssueCode.PartitionFailed,
                IssueSeverity.Warning,
                "A quarantined row group was re-fetched via gap fill (data-quality incident logged)."));
        }

        return new DataEnvelope<IReadOnlyList<TRecord>>(
            records, coverage, issues, provenance);
    }

    // Scan the registered IDatasetKeyBuilder<TRequest, TDatasetKey> descriptors for one whose first
    // type argument matches TRequest, and return its TDatasetKey. Returns null when none is
    // registered (the request type is not time-series-backed).
    private Type? ResolveDatasetKeyType(Type requestType)
    {
        // A builder is registered as IDatasetKeyBuilder<TRequest, TDatasetKey>; the DI container can
        // enumerate the concrete registrations. We probe the closed interface by testing candidate
        // key types would be brittle, so instead resolve via the container's registered services.
        var openBuilder = typeof(IDatasetKeyBuilder<,>);
        foreach (var candidate in EnumerateRegisteredKeyBuilderInterfaces())
        {
            if (candidate.GetGenericTypeDefinition() == openBuilder
                && candidate.GenericTypeArguments[0] == requestType)
            {
                return candidate.GenericTypeArguments[1];
            }
        }

        return null;
    }

    // The set of closed IDatasetKeyBuilder<,> interface types currently registered. Sourced from
    // the marker registry the kernel populates so this stays reflection-light and DI-container
    // agnostic (GetService on an open generic is not supported).
    private IEnumerable<Type> EnumerateRegisteredKeyBuilderInterfaces()
    {
        var catalog = _serviceProvider.GetService<IDatasetKeyBuilderCatalog>();
        return catalog?.RegisteredBuilderInterfaces ?? [];
    }

    // Discover the fetched-row type TFetched of the registered fan-out for a
    // (requestType, datasetKeyType, storedType) triple, or null when none is registered (the request
    // is not a fan-out record type). A fan-out is registered as
    // IDatasetKeyFanOut<TRequest, TDatasetKey, TFetched, TStored>; the pipeline knows the stored
    // TRecord (= TStored) but not TFetched (they differ for OptionQuote), so it matches on
    // arguments 0/1/3 and reads back argument 2. The body-discriminant fan-outs are the identity case
    // where the returned TFetched equals storedType.
    private Type? ResolveFanOutFetchedType(Type requestType, Type datasetKeyType, Type storedType)
    {
        var openFanOut = typeof(IDatasetKeyFanOut<,,,>);
        foreach (var candidate in EnumerateRegisteredFanOutInterfaces())
        {
            if (candidate.GetGenericTypeDefinition() == openFanOut
                && candidate.GenericTypeArguments[0] == requestType
                && candidate.GenericTypeArguments[1] == datasetKeyType
                && candidate.GenericTypeArguments[3] == storedType)
            {
                return candidate.GenericTypeArguments[2];
            }
        }

        return null;
    }

    // The set of closed IDatasetKeyFanOut<,,,> interface types currently registered, sourced from the
    // fan-out catalogue the kernel populates (the container cannot resolve an open generic). Empty when
    // no fan-out record type is registered.
    private IEnumerable<Type> EnumerateRegisteredFanOutInterfaces()
    {
        var catalog = _serviceProvider.GetService<IDatasetKeyFanOutCatalog>();
        return catalog?.RegisteredFanOutInterfaces ?? [];
    }
}

// Hosts that manually compose a time-series store without the kernel's checkpoint registration keep
// the existing volatile behaviour. AddMarketDataKernel supplies the durable implementation.
internal sealed class NoOpTimeSeriesRecoveryMetadataStore : ITimeSeriesRecoveryMetadataStore
{
    public static NoOpTimeSeriesRecoveryMetadataStore Instance { get; } = new();

    public Task<IReadOnlyList<TimeSeriesSourceMetadata>> GetSourcesAsync(
        string identity, DateRange range, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TimeSeriesSourceMetadata>>([]);

    public Task RecordSourceAsync(
        string identity, TimeSeriesSourceMetadata source, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<ProviderCode>> GetProvidersAsync(
        Type requestType,
        string datasetKey,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ProviderCode>>([]);

    public Task RecordProviderAsync(
        Type requestType,
        string datasetKey,
        ProviderCode provider,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<(DateOnly From, DateOnly To)>> GetAttemptedRangesAsync(
        string datasetIdentity,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<(DateOnly From, DateOnly To)>>([]);

    public Task RecordAttemptAsync(
        string datasetIdentity,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
