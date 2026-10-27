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
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Abstractions.Snapshots;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;

namespace Boutquin.MarketData.Orchestration.Snapshots;

/// <summary>
/// Read handle whose first read for each record type captures that type's engine watermark,
/// reused by subsequent reads of that type. Handle construction captures nothing; this is
/// not a cross-record-type atomic snapshot. Disposal releases captured snapshots in
/// acquisition-inverse (LIFO) order.
/// </summary>
/// <remarks>
/// <para><b>Supported types.</b> Only columnar-rehydratable types with a registered snapshot
/// binding participate. Row-only <c>DividendPayment</c> and <c>FuturesSettlement</c> are unsupported;
/// row-format stores for columnar-rehydratable types can still participate.</para>
/// <para><b>Not thread-safe.</b> Backed by a plain <see cref="Dictionary{TKey, TValue}"/> of
/// per-record snapshots; acquire one handle per execution context and issue
/// <see cref="ExecuteAsync{TRecord}"/> calls sequentially.</para>
/// <para><b>Gap fail-fast.</b> The snapshot path never dispatches an adapter; a gap raises
/// <see cref="SnapshotGapException"/>.</para>
/// </remarks>
public sealed class DataSnapshot : IDataSnapshot
{
    private readonly ITimeSeriesStoreRegistry _registry;
    private readonly Func<Type, object?> _bindingResolver;

    // Per-record captured snapshots, in acquisition order (LIFO dispose walks this in reverse).
    private readonly Dictionary<Type, IAsyncDisposable> _captured = [];
    private readonly List<Type> _acquisitionOrder = [];

    private bool _disposed;

    /// <summary>
    /// Initialises a new <see cref="DataSnapshot"/>.
    /// </summary>
    /// <param name="registry">The store registry exposing <c>ResolveEngine</c> for snapshot capture.</param>
    /// <param name="bindingResolver">
    /// Resolves the per-record <see cref="ISnapshotReadBinding{TRecord}"/> for a record type (e.g.
    /// <c>IServiceProvider.GetService</c>). Returns <see langword="null"/> when no binding is
    /// registered for the type.
    /// </param>
    public DataSnapshot(ITimeSeriesStoreRegistry registry, Func<Type, object?> bindingResolver)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(bindingResolver);

        _registry = registry;
        _bindingResolver = bindingResolver;
    }

    /// <inheritdoc />
    public async Task<DataEnvelope<IReadOnlyList<TRecord>>> ExecuteAsync<TRecord>(
        IDataRequest request,
        CancellationToken cancellationToken = default)
        where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);

        var binding = _bindingResolver(typeof(TRecord)) as ISnapshotReadBinding<TRecord>
            ?? throw new InvalidOperationException(
                $"No snapshot read binding is registered for record type '{typeof(TRecord).FullName}'. "
                + "Register one in DI alongside the time-series store for this record type.");

        // Capture-once per record type; reuse the watermark for the handle's whole lifetime.
        if (!_captured.TryGetValue(typeof(TRecord), out var snapshot))
        {
            snapshot = await binding.CaptureAsync(_registry, cancellationToken).ConfigureAwait(false);
            _captured[typeof(TRecord)] = snapshot;
            _acquisitionOrder.Add(typeof(TRecord));
        }

        var result = await binding.ReadAsync(snapshot, request, cancellationToken).ConfigureAwait(false);

        if (result.HasGap)
        {
            throw new SnapshotGapException(
                $"The requested range {request.Range.From:O}..{request.Range.To:O} for record type "
                + $"'{typeof(TRecord).FullName}' (dataset '{request.DatasetKey}') is not fully covered "
                + "by the read snapshot. A snapshot read fails fast on a gap and never dispatches an "
                + "adapter; compose an explicit live read if you need gap-fill semantics.");
        }

        return BuildEnvelope(request, result.Records);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Release captured snapshots in acquisition-inverse (LIFO) order, inside a try/finally
        // chain so one failing sub-dispose does not abandon the remaining segment-pin releases.
        List<Exception>? failures = null;
        for (var i = _acquisitionOrder.Count - 1; i >= 0; i--)
        {
            var type = _acquisitionOrder[i];
            try
            {
                await _captured[type].DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        _captured.Clear();
        _acquisitionOrder.Clear();

        if (failures is { Count: > 0 })
        {
            // A single failure propagates as-is; more than one aggregates.
            throw failures.Count == 1 ? failures[0] : new AggregateException(failures);
        }
    }

    private static DataEnvelope<IReadOnlyList<TRecord>> BuildEnvelope<TRecord>(
        IDataRequest request, IReadOnlyList<TRecord> records)
        where TRecord : IHasDate
    {
        var returned = records.Count;
        var coverage = new DataCoverage(
            RequestedPoints: returned,
            ReturnedPoints: returned,
            MissingPoints: 0,
            CoverageRatio: 1.0m);

        // Provenance for a snapshot read is best-effort at this tier: the engine persists records,
        // not their original per-fetch provenance, so RetrievalMode marks the read as Snapshot and
        // the source URL is dropped (served from the pinned view, not a live fetch). This path
        // does not reconstruct each record's original license or source.
        var provenance = new DataProvenance(
            Provider: new ProviderCode("snapshot"),
            Dataset: request.DatasetKey,
            LicenseFlag: LicenseType.Unknown,
            RetrievalMode: RetrievalMode.Snapshot,
            Freshness: FreshnessClass.EndOfDay,
            RetrievedAtUtc: DateTimeOffset.UtcNow,
            SourceUrl: null);

        return new DataEnvelope<IReadOnlyList<TRecord>>(
            records,
            coverage,
            [],
            [provenance]);
    }
}
