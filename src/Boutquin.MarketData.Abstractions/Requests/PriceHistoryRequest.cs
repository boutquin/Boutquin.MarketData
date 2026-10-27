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
    public IReadOnlyList<Symbol> Symbols
    {
        get;
        init => field = CopyAndValidate(value);
    } = CopyAndValidate(Symbols);

    /// <summary>
    /// Optional listing venue hint — when set, adapter-selection orchestrators can filter to
    /// sources that cover the specified exchange (e.g. Tiingo for XNYS/XNAS, TwelveData for
    /// XTSE). When <see langword="null"/> the request is venue-neutral and any adapter may
    /// serve it. Also scopes <see cref="DatasetKey"/> so two requests for the same ticker on
    /// different exchanges produce distinct cache entries.
    /// </summary>
    /// <remarks>
    /// Exposed as an <c>init</c> property rather than a primary-constructor parameter so
    /// that adding this field in 1.1.0 remains binary-compatible for consumers compiled
    /// against the 1.0.x primary-ctor signature. Set via object-initializer:
    /// <c>new PriceHistoryRequest(symbols, range) { Exchange = ExchangeCode.XTSE }</c>.
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
    public IReadOnlyList<Symbol> PartitionKeys => Symbols;

    /// <inheritdoc />
    /// <remarks>
    /// Preserves the symbol set, the optional <see cref="Exchange"/> venue hint, and the
    /// <see cref="DataFrequency"/>; only <see cref="IDataRequest.Range"/> is narrowed to the gap.
    /// Used by the time-series gap-fill to fetch just a missing sub-range.
    /// </remarks>
    public IDataRequest WithRange(DateRange range) =>
        new PriceHistoryRequest(Symbols, range, Frequency) { Exchange = Exchange };

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
                    "Price history symbols cannot contain a default or null-ticker symbol.",
                    nameof(symbols));
            }

            if (!distinctSymbols.Add(symbol))
            {
                throw new ArgumentException(
                    $"Price history symbols must be unique; ticker '{symbol.Ticker}' appears more than once.",
                    nameof(symbols));
            }
        }

        return [.. symbols];
    }
}
