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

using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// A per-record-type <b>cross-dataset</b> range scan over the time-series store's composite
/// <c>(dataset, date)</c> key space — the read primitive the per-row-discriminant fan-out merge needs
/// across matching dataset identities. The store facade
/// <c>ITimeSeriesStore.GetRangeAsync</c> reads one dataset key; a curve-as-of read must instead scan
/// <b>across</b> the N per-discriminant keys that share a prefix. Those keys sort contiguously by
/// design, so the scan is a single composite-key range read.
/// </summary>
/// <typeparam name="TDatasetKey">The dataset partition key type.</typeparam>
/// <typeparam name="TRecord">The dated record type.</typeparam>
/// <remarks>
/// This interface deliberately does NOT constrain <typeparamref name="TRecord"/> to
/// <c>IColumnarRehydratable</c>, so the generic <c>DataPipeline</c> gap-fill worker (which knows only
/// <c>ISerializable</c> + <c>IHasDate</c>) can resolve and call it. The concrete
/// <see cref="LsmCrossKeyScan{TDatasetKey, TRecord}"/> carries the columnar constraint and is
/// registered per record type where that constraint is satisfiable — the same split the
/// snapshot-read binding uses to reach <c>ITimeSeriesStoreRegistry.ResolveEngine</c>.
/// </remarks>
public interface ICrossKeyScan<TDatasetKey, TRecord>
    where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    where TRecord : ISerializable<TRecord>, IHasDate, new()
{
    /// <summary>
    /// Streams every stored <c>(dataset, record)</c> pair whose composite key falls in the inclusive
    /// range <c>[(lo, loDate), (hi, hiDate)]</c>, in composite-key order (dataset ascending, then
    /// date). Because the key compares dataset-first, datasets strictly between
    /// <paramref name="lo"/> and <paramref name="hi"/> are returned for <b>all</b> their dates; the
    /// caller filters by the date window and by a prefix-membership predicate.
    /// </summary>
    /// <param name="lo">Inclusive lower dataset-key bound.</param>
    /// <param name="loDate">Inclusive date bound applied at the <paramref name="lo"/> dataset boundary.</param>
    /// <param name="hi">Inclusive upper dataset-key bound.</param>
    /// <param name="hiDate">Inclusive date bound applied at the <paramref name="hi"/> dataset boundary.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An async stream of <c>(dataset, record)</c> pairs in composite-key order.</returns>
    IAsyncEnumerable<(TDatasetKey Dataset, TRecord Record)> ScanAsync(
        TDatasetKey lo,
        DateOnly loDate,
        TDatasetKey hi,
        DateOnly hiDate,
        CancellationToken cancellationToken = default);
}
