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
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>
/// Requests historical FX spot rates for one or more currency pairs over a date range.
/// </summary>
/// <param name="Pairs">The currency pairs to fetch (e.g., EUR/USD, USD/CAD).</param>
/// <param name="Range">The inclusive date range for the request.</param>
/// <param name="Frequency">The observation frequency. Defaults to <see cref="DataFrequency.Daily"/>.</param>
public sealed record FxHistoryRequest(
    IReadOnlyList<FxPair> Pairs,
    DateRange Range,
    DataFrequency Frequency = DataFrequency.Daily) : IDataRequest, IBatchDataRequest<FxPair>
{
    /// <summary>
    /// The currency pairs being requested, stored as a defensive copy so mutation of the
    /// caller's collection cannot change <see cref="DatasetKey"/> stability or record equality.
    /// </summary>
    public IReadOnlyList<FxPair> Pairs
    {
        get;
        init => field = CopyAndValidate(value);
    } = CopyAndValidate(Pairs);

    /// <summary>
    /// Optional regional hint — FX is OTC and venue-neutral at the data level, but the hint
    /// lets adapter orchestration bias toward a regional source (Bank of Canada for CAD pairs,
    /// ECB for EUR pairs, Frankfurter for ad-hoc lookups). When <see langword="null"/> any
    /// adapter may serve the request. Also scopes <see cref="DatasetKey"/>.
    /// </summary>
    /// <remarks>
    /// Exposed as an <c>init</c> property for 1.1.0 binary compatibility with 1.0.x consumers
    /// (notably <c>Boutquin.Trading.Recipes.BacktestDatasetBuilder</c> which constructs
    /// FxHistoryRequest via the 2/3-arg positional signature).
    /// </remarks>
    public ExchangeCode? Exchange { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Pairs are joined with <c>_</c> because the resulting string flows into
    /// <c>FileSystemObjectStore</c> keys, whose validator only accepts
    /// <c>[a-zA-Z0-9._\-/]</c>. <c>+</c> (the intuitive join character) is rejected
    /// there and collapses the L2 write-through on multi-pair batches.
    /// When <see cref="Exchange"/> is non-null the code is suffixed via <c>/</c> so the
    /// L2 cache naturally partitions into a sub-directory per venue; when null the key
    /// is unchanged so existing snapshots remain reachable.
    /// </remarks>
    public string DatasetKey
    {
        get
        {
            var pairsKey = string.Join("_", Pairs.OrderBy(p => p.ToString(), StringComparer.OrdinalIgnoreCase));
            return Exchange is null ? pairsKey : $"{pairsKey}/{Exchange.Value}";
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<FxPair> PartitionKeys => Pairs;

    /// <inheritdoc />
    /// <remarks>
    /// Preserves the currency-pair set, the optional <see cref="Exchange"/> venue hint, and the
    /// <see cref="DataFrequency"/>; only <see cref="IDataRequest.Range"/> is narrowed to the gap.
    /// Used by the time-series gap-fill to fetch just a missing sub-range.
    /// </remarks>
    public IDataRequest WithRange(DateRange range) =>
        new FxHistoryRequest(Pairs, range, Frequency) { Exchange = Exchange };

    private static IReadOnlyList<FxPair> CopyAndValidate(IReadOnlyList<FxPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        ArgumentOutOfRangeException.ThrowIfZero(pairs.Count);

        if (pairs.Any(pair => pair is null))
        {
            throw new ArgumentException("FX history pairs must not contain null elements.", nameof(pairs));
        }

        if (pairs.Distinct().Count() != pairs.Count)
        {
            throw new ArgumentException("FX history pairs must be unique.", nameof(pairs));
        }

        return [.. pairs];
    }
}
