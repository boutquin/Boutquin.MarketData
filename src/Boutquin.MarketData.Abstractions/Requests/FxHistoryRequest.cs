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
    public IReadOnlyList<FxPair> Pairs { get; init; } = [.. Pairs];

    /// <inheritdoc />
    public string DatasetKey =>
        string.Join("+", Pairs.OrderBy(p => p.ToString(), StringComparer.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IReadOnlyList<FxPair> PartitionKeys => Pairs;
}
