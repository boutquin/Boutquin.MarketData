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
using Boutquin.MarketData.Abstractions.Keys;
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
    public IReadOnlyList<Symbol> Symbols
    {
        get;
        init => field = CopyAndValidate(value);
    } = CopyAndValidate(Symbols);

    /// <summary>
    /// Optional listing venue hint — when set, adapter-selection orchestrators can filter to
    /// sources that cover the specified exchange (e.g. Tiingo for XNYS/XNAS, TwelveData for
    /// XTSE). When <see langword="null"/> the request is venue-neutral and any adapter may
    /// serve it. Also scopes <see cref="DatasetKey"/>.
    /// </summary>
    /// <remarks>
    /// Exposed as an <c>init</c> property for 1.1.0 binary compatibility with 1.0.x consumers.
    /// </remarks>
    public ExchangeCode? Exchange { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Tickers are ordered case-insensitively, then ordinally to break ties. Separator-free
    /// singletons retain their legacy text; lists and tickers containing <c>_</c> use a
    /// count-prefixed UTF-16 encoding. Ticker slashes are doubled before the optional
    /// <c>/</c> plus <see cref="Exchange"/> suffix, keeping opaque symbols distinct from venues.
    /// Upgrade using a fresh snapshot directory and restart to discard old L1 entries: legacy
    /// keys can contain aliased data even at an unchanged venue-qualified key. Re-fetch the data;
    /// no automatic migration or legacy-key fallback is performed. Existing files are left untouched.
    /// </remarks>
    public string DatasetKey
    {
        get
        {
            var symbolsKey = DividendDatasetIdentity.EncodeSymbolList(Symbols.Select(symbol => symbol.Ticker));
            return Exchange is null ? symbolsKey : $"{symbolsKey}/{Exchange.Value}";
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Metadata requests are point-in-time and have no meaningful range. The returned sentinel
    /// spans the full <see cref="DateOnly"/> representable range so that any
    /// caller asking "is date X covered?" gets an affirmative answer. Coverage calculators
    /// that enumerate this range (for example, per-day business-day counts) should special-case
    /// <see cref="InstrumentMetadataRequest"/> to avoid multi-million-iteration loops.
    /// </remarks>
    public DateRange Range => new(DateOnly.MinValue, DateOnly.MaxValue);

    /// <inheritdoc />
    public DataFrequency Frequency => DataFrequency.Daily;

    /// <inheritdoc />
    public IReadOnlyList<Symbol> PartitionKeys => Symbols;

    private static IReadOnlyList<Symbol> CopyAndValidate(IReadOnlyList<Symbol> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentOutOfRangeException.ThrowIfZero(symbols.Count);

        var distinctSymbols = new HashSet<Symbol>();
        foreach (var symbol in symbols)
        {
            if (symbol.Ticker is null)
            {
                throw new ArgumentException(
                    "Instrument metadata symbols cannot contain a default or null-ticker symbol.",
                    nameof(symbols));
            }

            if (!distinctSymbols.Add(symbol))
            {
                throw new ArgumentException(
                    $"Instrument metadata symbols must be unique; ticker '{symbol.Ticker}' appears more than once.",
                    nameof(symbols));
            }
        }

        return [.. symbols];
    }
}
