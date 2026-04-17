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
/// Actual/360 day-count convention — divides actual elapsed calendar days by a fixed
/// 360-day year. Dominant basis for USD, EUR, and CHF money markets.
/// </summary>
/// <remarks>
/// <para>
/// Formula (ISDA 2006 Definitions, Section 4.16(e)):
/// <c>yf = (endDate − startDate) / 360</c> counting actual calendar days inclusive of
/// weekends and holidays. A full 365-day calendar year therefore produces a fraction of
/// <c>365/360 ≈ 1.01389</c>, not 1.0 — callers comparing accrual to "one year" must use
/// the convention's own reciprocal, not a calendar year.
/// </para>
/// <para>
/// Pipeline role: applied to LIBOR, SOFR, Fed Funds, EURIBOR, and most other short-term
/// rate indices. Consumers should resolve through <see cref="DayCountResolver.Resolve"/> so
/// the mapping from market convention to calculator stays centralized.
/// </para>
/// </remarks>
public sealed class Actual360 : IYearFractionCalculator
{
    /// <summary>
    /// Shared singleton. The convention is stateless so a single instance is safe for
    /// concurrent use across the process.
    /// </summary>
    public static readonly Actual360 Instance = new();

    private Actual360() { }

    /// <inheritdoc />
    public string Code => "ACT/360";

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="endDate"/> is earlier than <paramref name="startDate"/>.
    /// </exception>
    public double YearFraction(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endDate),
                $"endDate ({endDate:O}) must be on or after startDate ({startDate:O}).");
        }

        return (endDate.DayNumber - startDate.DayNumber) / 360.0;
    }
}
