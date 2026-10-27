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
/// Contract identity (<c>Underlying</c>, <c>Expiry</c>, <c>Strike</c>, <c>Type</c>) lives in
/// <c>OptionQuoteDatasetKey</c>, not the record body, so this normalizer operates on the slimmed
/// body <c>(Date, Bid?, Ask?, LastPrice?, ImpliedVolatility?, OpenInterest, Volume)</c>. <c>Strike</c>
/// rounding is therefore dropped here — strike precision is governed by <c>[DecimalScale(4)]</c> on
/// the key's ordered wire, not by this normalizer.
/// </para>
/// <para>
/// The implied-volatility conversion is a representation heuristic, not a range check. A genuine
/// decimal volatility greater than or equal to <c>1.0</c> (100%) is indistinguishable from a
/// percent-form value and will be divided by 100. Adapters for markets where such values are valid
/// should emit a known representation and replace or bypass this normalizer.
/// </para>
/// </remarks>
public sealed class OptionQuoteNormalizer : INormalizer<OptionQuote, OptionQuote>
{
    private readonly int _decimalPlaces;

    /// <summary>
    /// Initializes a new <see cref="OptionQuoteNormalizer"/>.
    /// </summary>
    /// <param name="decimalPlaces">Decimal places for price rounding. Defaults to 4.</param>
    /// <exception cref="ArgumentOutOfRangeException">Precision is outside the decimal rounding range of 0 through 28.</exception>
    public OptionQuoteNormalizer(int decimalPlaces = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimalPlaces);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimalPlaces, 28);
        _decimalPlaces = decimalPlaces;
    }

    /// <inheritdoc />
    public OptionQuote Normalize(OptionQuote raw)
    {
        NormalizerInputGuard.ThrowIfNull(raw);

        return new OptionQuote(
            raw.Date,
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
