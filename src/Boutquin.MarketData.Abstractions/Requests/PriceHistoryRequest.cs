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
using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>
/// Requests historical price bars for one or more equity/ETF symbols over a date range.
/// </summary>
/// <param name="Symbols">The strongly-typed ticker symbols to fetch.</param>
/// <param name="Range">The inclusive date range for the request.</param>
/// <param name="Frequency">The bar frequency. Defaults to <see cref="DataFrequency.Daily"/>.</param>
public sealed record PriceHistoryRequest(
    IReadOnlyList<Symbol> Symbols,
    DateRange Range,
    DataFrequency Frequency = DataFrequency.Daily) : IDataRequest, IBatchDataRequest<Symbol>
{
    /// <summary>
    /// The ticker symbols being requested, stored as a defensive copy so mutation of the
    /// caller's collection cannot change <see cref="DatasetKey"/> stability or record equality.
    /// </summary>
    public IReadOnlyList<Symbol> Symbols { get; init; } = [.. Symbols];

    /// <inheritdoc />
    public string DatasetKey =>
        string.Join("+", Symbols.Select(s => s.Ticker).OrderBy(t => t, StringComparer.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IReadOnlyList<Symbol> PartitionKeys => Symbols;
}
