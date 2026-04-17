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
/// Actual/365 Fixed day-count convention — divides actual elapsed calendar days by a fixed
/// 365-day year regardless of leap years. Market basis for GBP and CAD interest rates and
/// many Commonwealth government bonds.
/// </summary>
/// <remarks>
/// <para>
/// Formula (ISDA 2006 Definitions, Section 4.16(d)):
/// <c>yf = (endDate − startDate) / 365</c> counting actual calendar days. The denominator is
/// fixed at 365 even in leap years — that is the "Fixed" qualifier and the distinction from
/// ACT/ACT, which would use 366 across a leap day. For a period spanning Feb 29 this
/// convention produces a slightly higher fraction than ACT/ACT.
/// </para>
/// <para>
/// Pipeline role: used when the dataset or instrument specifies 365 Fixed explicitly (for
/// example GBP LIBOR legacy fallbacks, SONIA-referenced accruals, CORRA-referenced CAD
/// products). Resolve through <see cref="DayCountResolver.Resolve"/> to benefit from the
/// alias mapping and keep consumer code independent of the specific class name.
/// </para>
/// </remarks>
public sealed class Actual365Fixed : IYearFractionCalculator
{
    /// <summary>
    /// Shared singleton. The convention is stateless so a single instance is safe for
    /// concurrent use across the process.
    /// </summary>
    public static readonly Actual365Fixed Instance = new();

    private Actual365Fixed() { }

    /// <inheritdoc />
    public string Code => "ACT/365F";

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

        return (endDate.DayNumber - startDate.DayNumber) / 365.0;
    }
}
