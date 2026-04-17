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

namespace Boutquin.MarketData.DayCount;

/// <summary>
/// Maps a market day-count code string (and its common aliases) to the corresponding
/// <see cref="IYearFractionCalculator"/> singleton.
/// </summary>
/// <remarks>
/// <para>
/// Domain context: day-count conventions are standardised by ISDA (2006 Definitions, Section 4.16)
/// and by the market segments that adopted them. Five conventions ship in the MarketData kernel:
/// ACT/360, ACT/365F, 30/360 (Bond Basis), ACT/ACT in-period (ICMA Rule 251), and 30E/360
/// (Eurobond). Additional ISDA conventions (ACT/ACT ISDA, BUS/252, NL/365) can be added by
/// extending the switch expression — they are deliberately omitted until a concrete consumer
/// requires them, since each new convention carries its own corner-case tests.
/// </para>
/// <para>
/// Alias policy: the resolver accepts both the canonical ISDA codes and widely used alternative
/// spellings. Matching is case-insensitive; unknown codes throw rather than falling back to a
/// default, because silently substituting a convention would produce wrong accrual values
/// without any visible failure.
/// </para>
/// <para>
/// ACT/ACT disambiguation: <c>"ACT/ACT"</c> without further qualification resolves to ICMA
/// (in-period) since that is the convention most new consumers reach for. The ISDA variant
/// is not yet shipped; use an <c>"ACT/ACT ISDA"</c> alias when that lands in a later spec.
/// </para>
/// </remarks>
public static class DayCountResolver
{
    /// <summary>
    /// Returns the <see cref="IYearFractionCalculator"/> matching the given market code.
    /// </summary>
    /// <param name="code">
    /// Case-insensitive day-count code. Accepted values:
    /// <list type="bullet">
    ///   <item><description><c>ACT/360</c>, <c>ACTUAL/360</c> — money markets.</description></item>
    ///   <item><description><c>ACT/365F</c>, <c>ACT/365</c>, <c>ACTUAL/365F</c> — GBP/CAD rates, many government bonds.</description></item>
    ///   <item><description><c>30/360</c>, <c>THIRTY/360</c> — US corporate and government bond basis.</description></item>
    ///   <item><description><c>ACT/ACT</c>, <c>ACTUAL/ACTUAL</c>, <c>ACT/ACT ICMA</c>, <c>ACT/ACT BOND</c> — US Treasury / euro government bonds.</description></item>
    ///   <item><description><c>30E/360</c>, <c>EUROBOND</c>, <c>EUROBOND BASIS</c> — euro corporate bonds.</description></item>
    /// </list>
    /// </param>
    /// <returns>The singleton calculator for the requested convention. Instances are shared so callers can reuse the reference safely.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="code"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="code"/> does not match a supported convention; the exception message contains the rejected code for diagnostics.</exception>
    public static IYearFractionCalculator Resolve(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        // Trim and upper-case so config-file whitespace and casing variants resolve identically.
        return code.Trim().ToUpperInvariant() switch
        {
            "ACT/360" or "ACTUAL/360" => Actual360.Instance,
            "ACT/365F" or "ACT/365" or "ACTUAL/365F" => Actual365Fixed.Instance,
            "30/360" or "THIRTY/360" => Thirty360.Instance,
            "ACT/ACT" or "ACTUAL/ACTUAL" or "ACT/ACT ICMA" or "ACT/ACT BOND"
                => ActualActualInPeriod.Instance,
            "30E/360" or "EUROBOND" or "EUROBOND BASIS" => ThirtyE360.Instance,
            _ => throw new NotSupportedException($"Unknown day-count convention: {code}"),
        };
    }
}
