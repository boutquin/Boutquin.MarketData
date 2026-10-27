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

using System.Runtime.CompilerServices;

using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Row-format <see cref="ICrossKeyScan{TDatasetKey, TRecord}"/>: the read dual of the per-row fan-out
/// append for a <b>row-format</b> record (the keyed-row envelope's stored <c>OptionQuote</c>, which has
/// no columnar engine). It resolves the record type's engine via
/// <see cref="ITimeSeriesStoreRegistry.ResolveScanEngine{TDatasetKey, TRecord}"/> and issues a single
/// composite-key range read through <c>ILsmStorageEngine.GetRangeAsync</c>, enumerating every
/// per-discriminant key that shares a prefix, in key order.
/// </summary>
/// <typeparam name="TDatasetKey">The dataset partition key type.</typeparam>
/// <typeparam name="TRecord">The dated record type (row-format; no columnar constraint).</typeparam>
/// <remarks>
/// This is the row-store sibling of <see cref="LsmCrossKeyScan{TDatasetKey, TRecord}"/>. The columnar
/// scan resolves through <see cref="ITimeSeriesStoreRegistry.ResolveEngine{TDatasetKey, TRecord}"/>
/// (which requires <c>IColumnarRehydratable</c> for the snapshot path); a row-format fan-out record
/// cannot satisfy that constraint, so this type resolves through the row-safe
/// <see cref="ITimeSeriesStoreRegistry.ResolveScanEngine{TDatasetKey, TRecord}"/> instead. The scan
/// itself is identical — a composite-key range read is format-agnostic.
/// </remarks>
public sealed class RowLsmCrossKeyScan<TDatasetKey, TRecord> : ICrossKeyScan<TDatasetKey, TRecord>
    where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    where TRecord : ISerializable<TRecord>, IHasDate, new()
{
    private readonly ITimeSeriesStoreRegistry _registry;

    /// <summary>
    /// Creates the scan over the store <paramref name="registry"/>. The engine is resolved lazily per
    /// <see cref="ScanAsync"/> call so the scan holds no engine reference across the registry's own
    /// lifetime management.
    /// </summary>
    /// <param name="registry">The registry exposing <c>ResolveScanEngine</c> for the record type.</param>
    public RowLsmCrossKeyScan(ITimeSeriesStoreRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<(TDatasetKey Dataset, TRecord Record)> ScanAsync(
        TDatasetKey lo,
        DateOnly loDate,
        TDatasetKey hi,
        DateOnly hiDate,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var engine = _registry.ResolveScanEngine<TDatasetKey, TRecord>()
            ?? throw new InvalidOperationException(
                $"No time-series scan engine is registered for record type '{typeof(TRecord).FullName}' "
                + $"(dataset key '{typeof(TDatasetKey).FullName}'). Register one via "
                + "IMutableTimeSeriesStoreRegistry.RegisterScanEngine during DI setup before a fan-out "
                + "cross-key read for this record type.");
        var start = new TimeSeriesKey<TDatasetKey>(lo, loDate);
        var end = new TimeSeriesKey<TDatasetKey>(hi, hiDate);

        await foreach (var (key, value) in engine.GetRangeAsync(start, end, cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return (key.Dataset, value);
        }
    }
}
