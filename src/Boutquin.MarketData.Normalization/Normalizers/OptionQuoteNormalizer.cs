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

namespace Boutquin.MarketData.Normalization.Normalizers;

/// <summary>
/// Normalizes <see cref="OptionQuote"/> records by converting percent-form implied
/// volatility to decimal, rounding prices, and clamping negative volume/open
/// interest to zero.
/// </summary>
/// <remarks>
/// <para>
/// Implied volatility heuristic: values &gt;= 1 are treated as percent (e.g., 25.0 → 0.25).
/// Values &lt; 1 are assumed to already be in decimal form.
/// </para>
/// <para>
/// Underlying is strongly-typed (<c>Symbol</c>) after the reference-data
/// consolidation spec — casing normalization is no longer applicable at this layer
/// because the <c>Symbol</c> constructor already rejects whitespace.
/// </para>
/// </remarks>
public sealed class OptionQuoteNormalizer : INormalizer<OptionQuote, OptionQuote>
{
    private readonly int _decimalPlaces;

    /// <summary>
    /// Initializes a new <see cref="OptionQuoteNormalizer"/>.
    /// </summary>
    /// <param name="decimalPlaces">Decimal places for price rounding. Defaults to 4.</param>
    public OptionQuoteNormalizer(int decimalPlaces = 4)
    {
        _decimalPlaces = decimalPlaces;
    }

    /// <inheritdoc />
    public OptionQuote Normalize(OptionQuote raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        return new OptionQuote(
            raw.Date,
            raw.Underlying,
            raw.Expiry,
            Round(raw.Strike),
            raw.Type,
            raw.Bid.HasValue ? Round(raw.Bid.Value) : null,
            raw.Ask.HasValue ? Round(raw.Ask.Value) : null,
            raw.LastPrice.HasValue ? Round(raw.LastPrice.Value) : null,
            NormalizeIv(raw.ImpliedVolatility),
            Math.Max(0, raw.OpenInterest),
            Math.Max(0, raw.Volume));
    }

    private decimal Round(decimal value) =>
        Math.Round(value, _decimalPlaces, MidpointRounding.AwayFromZero);

    private static decimal? NormalizeIv(decimal? iv)
    {
        if (iv is null)
        {
            return null;
        }

        // Heuristic: IV >= 1 is percent form (e.g., 25.0 → 0.25)
        return iv.Value >= 1m ? iv.Value / 100m : iv.Value;
    }
}
