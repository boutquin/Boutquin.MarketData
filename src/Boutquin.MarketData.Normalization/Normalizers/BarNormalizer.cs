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
/// Produces a canonical <see cref="Bar"/> by rounding OHLC prices to a configurable precision,
/// clamping negative volume to zero, and swapping inverted high/low values.
/// Open and close are not constrained to the resulting high/low interval.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline role: default normalizer for every adapter emitting <see cref="Bar"/> records. Runs
/// after fetch and before caching so snapshots store canonical values, not vendor-formatted
/// ones. The normalizer is deterministic and stateless; the same input always produces the
/// same output regardless of call order.
/// </para>
/// <para>
/// Transformations:
/// <list type="number">
///   <item><description>Round OHLC and adjusted-close to <c>decimalPlaces</c> using
///   <see cref="MidpointRounding.AwayFromZero"/>. Banker's rounding is deliberately avoided
///   so a vendor-supplied 0.05 always rounds to 0.1, not alternately to 0.0 / 0.1 depending
///   on the preceding digit — important for reproducibility across runs.</description></item>
///   <item><description>Swap <c>High</c> and <c>Low</c> when <c>High &lt; Low</c>. Several
///   historical providers occasionally emit inverted pairs due to upstream merge bugs.
///   This ensures <c>Low ≤ High</c> but does not validate open or close against that interval.</description></item>
///   <item><description>Clamp <c>Volume</c> to <c>[0, +∞)</c>. Negative volumes are data
///   defects, usually from signed deltas leaking into cumulative fields.</description></item>
/// </list>
/// </para>
/// <para>
/// Precision default: 4 decimal places. Chosen because it preserves all US cash-equity tick
/// increments (penny to sub-penny), most FX crosses, and ETF NAV reporting. Override with
/// a larger value for FX majors quoted to 5 decimals or with a smaller value for futures
/// settlement prices reported to 2 decimals.
/// </para>
/// <para>
/// Not handled here: adjusted-close split/dividend alignment, timezone normalization, volume
/// unit conversion (shares vs contracts). Those belong to dedicated processors upstream of
/// the normalizer, where the corporate-action history and contract specs are available.
/// </para>
/// </remarks>
public sealed class BarNormalizer : INormalizer<Bar, Bar>
{
    private readonly int _decimalPlaces;

    /// <summary>
    /// Captures the rounding precision applied to OHLC and adjusted-close prices.
    /// </summary>
    /// <param name="decimalPlaces">
    /// Number of decimal places used for price rounding. Defaults to 4 (cents-to-sub-cents for
    /// cash equities and ETF NAVs). Raise to 5-6 for FX majors quoted in pips; lower to 2 for
    /// futures settlement prices.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Precision is outside the decimal rounding range of 0 through 28.</exception>
    public BarNormalizer(int decimalPlaces = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimalPlaces);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimalPlaces, 28);
        _decimalPlaces = decimalPlaces;
    }

    /// <inheritdoc />
    public Bar Normalize(Bar raw)
    {
        NormalizerInputGuard.ThrowIfNull(raw);

        var open = Math.Round(raw.Open, _decimalPlaces, MidpointRounding.AwayFromZero);
        var high = Math.Round(raw.High, _decimalPlaces, MidpointRounding.AwayFromZero);
        var low = Math.Round(raw.Low, _decimalPlaces, MidpointRounding.AwayFromZero);
        var close = Math.Round(raw.Close, _decimalPlaces, MidpointRounding.AwayFromZero);
        var adjClose = Math.Round(raw.AdjustedClose, _decimalPlaces, MidpointRounding.AwayFromZero);

        // Swap rather than reject: a bar with inverted high/low is still usable for
        // close-to-close analytics; raising an exception here would abort normalization of the
        // entire batch for what is typically a single-vendor glitch on a single trading day.
        if (high < low)
        {
            (high, low) = (low, high);
        }

        var volume = Math.Max(0, raw.Volume);

        return new Bar(raw.Date, open, high, low, close, adjClose, volume);
    }
}
