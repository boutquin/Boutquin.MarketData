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
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// LSM-backed <see cref="ICrossKeyScan{TDatasetKey, TRecord}"/>: resolves the record type's engine via
/// <see cref="ITimeSeriesStoreRegistry.ResolveEngine{TDatasetKey, TRecord}"/> and issues a single
/// composite-key range read through <c>ILsmStorageEngine.GetRangeAsync</c>. This is the read dual of
/// the per-tenor fan-out append: one <c>(dataset, date)</c> range read enumerates every
/// per-discriminant key that shares a prefix, in key order.
/// </summary>
/// <typeparam name="TDatasetKey">The dataset partition key type.</typeparam>
/// <typeparam name="TRecord">The dated, columnar-rehydratable record type.</typeparam>
/// <remarks>
/// The <see cref="IColumnarRehydratable{TSelf}"/> constraint mirrors
/// <see cref="ITimeSeriesStoreRegistry.ResolveEngine{TDatasetKey, TRecord}"/>; it is satisfiable at
/// the per-record registration site (every fan-out record type is <c>[ColumnarStorable]</c>), so this
/// concrete type is registered there while the pipeline resolves the unconstrained
/// <see cref="ICrossKeyScan{TDatasetKey, TRecord}"/> — the same constraint-hiding split the
/// production snapshot-read binding uses.
/// </remarks>
public sealed class LsmCrossKeyScan<TDatasetKey, TRecord> : ICrossKeyScan<TDatasetKey, TRecord>
    where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
{
    private readonly ITimeSeriesStoreRegistry _registry;

    /// <summary>
    /// Creates the scan over the store <paramref name="registry"/>. The engine is resolved lazily per
    /// <see cref="ScanAsync"/> call so the scan holds no engine reference across the registry's own
    /// lifetime management.
    /// </summary>
    /// <param name="registry">The registry exposing <c>ResolveEngine</c> for the record type.</param>
    public LsmCrossKeyScan(ITimeSeriesStoreRegistry registry)
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
        var engine = _registry.ResolveEngine<TDatasetKey, TRecord>();
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
