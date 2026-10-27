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

using Boutquin.MarketData.Abstractions.Calendars;
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.Calendars;
using Boutquin.MarketData.Orchestration.Pipeline;

using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;

namespace Boutquin.MarketData.Orchestration.Snapshots;

/// <summary>
/// Production <see cref="ISnapshotReadBinding{TRecord}"/> backed by the real Boutquin.Storage LSM
/// engine. Registered per <c>(TDatasetKey, TRecord)</c> pair by
/// <c>AddTimeSeriesStore</c>; resolved from DI by <see cref="DataSnapshot"/> on the first
/// <c>ExecuteAsync&lt;TRecord&gt;</c> call for the record type. Reads use the captured engine snapshot
/// so later writes cannot change the observations visible through that snapshot.
/// </summary>
/// <typeparam name="TDatasetKey">Dataset partition key type.</typeparam>
/// <typeparam name="TRecord">The dated, columnar-rehydratable record type.</typeparam>
/// <remarks>
/// <para><b>Capture.</b> <see cref="CaptureAsync"/> resolves the engine for
/// <typeparamref name="TRecord"/> via
/// <see cref="ITimeSeriesStoreRegistry.ResolveEngine{TDatasetKey, TRecord}"/> and pins a fresh
/// point-in-time snapshot through <c>AcquireSnapshotAsync</c>. <see cref="DataSnapshot"/> caches the
/// returned handle once per record type and reuses it for the handle's lifetime.</para>
/// <para><b>Read.</b> <see cref="ReadAsync"/> projects the request into a
/// <typeparamref name="TDatasetKey"/> via the DI-registered
/// <see cref="IDatasetKeyBuilder{TRequest, TDatasetKey}"/> — the same dataset-key projection the
/// live pipeline uses — then range-scans the request's date span through the captured snapshot.
/// It performs NO adapter dispatch: a gap is reported back to <see cref="DataSnapshot"/>, which
/// raises the <c>SnapshotGapException</c>.</para>
/// <para><b>Columnar limitation.</b> The columnar segment format rejects
/// <c>AcquireSnapshotAsync</c>; a snapshot read against a columnar-registered record therefore
/// surfaces that upstream Storage limitation from <see cref="CaptureAsync"/> (NOT a missing-binding
/// error — the binding is always registered). A record that must serve snapshot reads opts into the
/// row format via <c>AddTimeSeriesStore(b =&gt; b.UseRowFormat())</c>.</para>
/// </remarks>
internal sealed class LsmSnapshotReadBinding<TDatasetKey, TRecord> : ISnapshotReadBinding<TRecord>
    where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
{
    // Weekend-only fallback when the container registers no IBusinessCalendar (the composition root
    // registers one, so this only guards hand-built providers). Weekends are universal; a
    // market-specific calendar registered in DI additionally excludes holidays.
    private static readonly IBusinessCalendar s_defaultCalendar = new WeekendOnlyCalendar();

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Creates the binding. The <paramref name="serviceProvider"/> is used at read time to resolve
    /// the per-request <see cref="IDatasetKeyBuilder{TRequest, TDatasetKey}"/> matching the request's
    /// concrete runtime type — the same builder the live pipeline resolves.
    /// </summary>
    /// <param name="serviceProvider">The kernel service provider carrying the dataset-key builders.</param>
    public LsmSnapshotReadBinding(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async Task<IAsyncDisposable> CaptureAsync(
        ITimeSeriesStoreRegistry registry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var engine = registry.ResolveEngine<TDatasetKey, TRecord>();
        return await engine.AcquireSnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SnapshotReadResult<TRecord>> ReadAsync(
        IAsyncDisposable snapshot, IDataRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(request);

        var snap = (ISnapshot<TimeSeriesKey<TDatasetKey>, TRecord>)snapshot;

        var from = request.Range.From;
        var to = request.Range.To;

        var catalog = (IDatasetKeyFanOutCatalog?)_serviceProvider.GetService(typeof(IDatasetKeyFanOutCatalog));
        var fanOutType = catalog?.RegisteredFanOutInterfaces.FirstOrDefault(candidate =>
            candidate.GetGenericTypeDefinition() == typeof(IDatasetKeyFanOut<,,,>)
            && candidate.GenericTypeArguments[0] == request.GetType()
            && candidate.GenericTypeArguments[1] == typeof(TDatasetKey)
            && candidate.GenericTypeArguments[3] == typeof(TRecord));
        if (fanOutType is not null)
        {
            var read = typeof(LsmSnapshotReadBinding<TDatasetKey, TRecord>)
                .GetMethod(nameof(ReadFanOutAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(request.GetType(), fanOutType.GenericTypeArguments[2]);
            return await ((Task<SnapshotReadResult<TRecord>>)read.Invoke(
                this, [snap, request, cancellationToken])!).ConfigureAwait(false);
        }

        // A request that maps to MORE THAN ONE dataset key (a multi-symbol / multi-pair batch) must be
        // read per partition and merged — mirroring the live pipeline's partition-first gap-fill.
        // Building a single key from the whole request would key on PartitionKeys[0] and silently
        // return only that partition's records. Single-partition / non-batch requests read the
        // one key directly, exactly as before.
        var datasetKeys = BatchRequestSplitter.TrySplit(request, out var partitionRequests)
            ? partitionRequests.Select(BuildDatasetKey).ToList()
            : [BuildDatasetKey(request)];

        // Use the same frequency-aware coverage contract as live requests. Daily data is measured
        // against the business calendar; weekly/monthly data uses its requested observation count.
        // Every partition must independently cover the range.
        var calendar = (IBusinessCalendar?)_serviceProvider.GetService(typeof(IBusinessCalendar))
            ?? s_defaultCalendar;

        var records = new List<TRecord>();
        var hasGap = false;
        foreach (var datasetKey in datasetKeys)
        {
            var presentDates = new HashSet<DateOnly>();
            await foreach (var kv in snap.GetRangeAsync(
                new TimeSeriesKey<TDatasetKey>(datasetKey, from),
                new TimeSeriesKey<TDatasetKey>(datasetKey, to),
                cancellationToken).ConfigureAwait(false))
            {
                records.Add(kv.Value);
                presentDates.Add(kv.Value.Date);
            }

            hasGap |= CoverageComputation.ComputeWithGaps(
                calendar, request.Range, request.Frequency, presentDates).Coverage.MissingPoints > 0;
        }

        return new SnapshotReadResult<TRecord>(records, hasGap);
    }

    private async Task<SnapshotReadResult<TRecord>> ReadFanOutAsync<TRequest, TFetched>(
        ISnapshot<TimeSeriesKey<TDatasetKey>, TRecord> snapshot,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IDataRequest
    {
        var fanOut = (IDatasetKeyFanOut<TRequest, TDatasetKey, TFetched, TRecord>)
            (_serviceProvider.GetService(typeof(IDatasetKeyFanOut<TRequest, TDatasetKey, TFetched, TRecord>))
                ?? throw new InvalidOperationException("The registered snapshot fan-out service is missing."));
        var (lo, hi) = fanOut.PrefixBounds(request);
        var records = new List<TRecord>();
        var datesByKey = new Dictionary<TDatasetKey, HashSet<DateOnly>>();
        // Discover every known discriminant through the captured view, including keys whose only
        // visible rows lie outside this request. Otherwise a wholly missing tenor/date is invisible.
        await foreach (var entry in snapshot.GetRangeAsync(
            new TimeSeriesKey<TDatasetKey>(lo, DateOnly.MinValue),
            new TimeSeriesKey<TDatasetKey>(hi, DateOnly.MaxValue),
            cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = entry.Key.Dataset;
            if (!fanOut.KeyMatchesRequest(request, key))
            {
                continue;
            }

            if (!datesByKey.TryGetValue(key, out var dates))
            {
                dates = [];
                datesByKey.Add(key, dates);
            }

            if (entry.Value.Date >= request.Range.From && entry.Value.Date <= request.Range.To)
            {
                records.Add(entry.Value);
                dates.Add(entry.Value.Date);
            }
        }

        var calendar = (IBusinessCalendar?)_serviceProvider.GetService(typeof(IBusinessCalendar))
            ?? s_defaultCalendar;
        var hasGap = datesByKey.Count == 0
            ? CoverageComputation.CountExpectedPoints(calendar, request.Range, request.Frequency) > 0
            : datesByKey.Values.Any(dates => CoverageComputation.ComputeWithGaps(
                calendar, request.Range, request.Frequency, dates).Coverage.MissingPoints > 0);
        return new SnapshotReadResult<TRecord>(records, hasGap);
    }

    /// <summary>
    /// Projects the request into its <typeparamref name="TDatasetKey"/> via the DI-registered
    /// <see cref="IDatasetKeyBuilder{TRequest, TDatasetKey}"/> for the request's concrete runtime
    /// type. The builder is the same one the live pipeline resolves; resolving it by runtime type
    /// keeps the binding generic over <typeparamref name="TDatasetKey"/> without carrying the
    /// request type as a third type parameter.
    /// </summary>
    private TDatasetKey BuildDatasetKey(IDataRequest request)
    {
        var builderType = typeof(IDatasetKeyBuilder<,>).MakeGenericType(request.GetType(), typeof(TDatasetKey));
        var builder = _serviceProvider.GetService(builderType)
            ?? throw new InvalidOperationException(
                $"No IDatasetKeyBuilder<{request.GetType().Name}, {typeof(TDatasetKey).Name}> is registered. "
                + "A snapshot read needs the same dataset-key builder the live pipeline uses to project "
                + "the request into its dataset key; register it alongside the time-series store.");

        // Build(request) is the single method on IDatasetKeyBuilder<TRequest, TDatasetKey>; invoking
        // it through the open-generic-resolved instance returns the strongly-typed key.
        var build = builderType.GetMethod(nameof(IDatasetKeyBuilder<IDataRequest, TDatasetKey>.Build))!;
        return (TDatasetKey)build.Invoke(builder, [request])!;
    }
}
