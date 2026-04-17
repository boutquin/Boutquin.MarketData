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
/// Requests instrument reference metadata for one or more ticker symbols.
/// </summary>
/// <remarks>
/// Unlike time-series requests, metadata is point-in-time and does not have a meaningful
/// date range. The <see cref="Range"/> and <see cref="Frequency"/> properties use sentinel
/// values to satisfy the <see cref="IDataRequest"/> contract.
/// </remarks>
/// <param name="Symbols">The strongly-typed ticker symbols to look up.</param>
public sealed record InstrumentMetadataRequest(
    IReadOnlyList<Symbol> Symbols) : IDataRequest, IBatchDataRequest<Symbol>
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
    /// <remarks>
    /// Metadata requests are point-in-time and have no meaningful range. The returned sentinel
    /// spans the full <see cref="DateOnly"/> representable range so that any
    /// caller asking "is date X covered?" gets an affirmative answer. Coverage calculators
    /// that enumerate this range (for example, per-day business-day counts) should special-case
    /// <see cref="InstrumentMetadataRequest"/> to avoid multi-billion-iteration loops.
    /// </remarks>
    public DateRange Range => new(DateOnly.MinValue, DateOnly.MaxValue);

    /// <inheritdoc />
    public DataFrequency Frequency => DataFrequency.Daily;

    /// <inheritdoc />
    public IReadOnlyList<Symbol> PartitionKeys => Symbols;
}
