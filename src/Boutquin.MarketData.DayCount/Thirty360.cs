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
/// 30/360 (Bond Basis) day-count convention — treats every month as 30 days and every year
/// as 360 days. Standard basis for US corporate and government bonds.
/// </summary>
/// <remarks>
/// <para>
/// Formula (ISDA 2006 Definitions, Section 4.16(f), "Bond Basis"):
/// <c>yf = (360·(Y₂ − Y₁) + 30·(M₂ − M₁) + (d₂ − d₁)) / 360</c>
/// with day-capping rules <c>d₁ = min(D₁, 30)</c> and
/// <c>d₂ = 30 if (D₂ = 31 and d₁ = 30) else D₂</c>.
/// </para>
/// <para>
/// The conditional D₂ capping is what distinguishes Bond Basis from 30E/360 (Eurobond):
/// 30/360 Bond Basis caps D₂ at 30 only when D₁ was already 30 or 31 (so the conditional
/// fires whenever the <em>capped</em> d₁ equals 30), leaving cases like Jan 15 → Jul 31 with
/// D₂ = 31 intact. 30E/360 caps D₂ at 30 unconditionally. The two conventions therefore
/// disagree on any span where the end date is the 31st and the start date is earlier than
/// the 30th — a common case for corporate-bond schedules rolling to month-end.
/// </para>
/// <para>
/// Pipeline role: used by coupon-schedule generators and accrual calculators that consume
/// yield-curve output. Consumers should call this via <see cref="DayCountResolver.Resolve"/>
/// rather than taking a direct dependency, so convention swaps (for example moving a new
/// product from 30/360 to ACT/360) are a single-line configuration change.
/// </para>
/// <para>
/// Contract: <c>startDate</c> must be on or before <c>endDate</c>.
/// Passing <c>endDate &lt; startDate</c> throws <see cref="ArgumentOutOfRangeException"/>
/// rather than returning a negative year fraction, because a negative accrual is almost
/// always a schedule-generation bug.
/// </para>
/// </remarks>
public sealed class Thirty360 : IYearFractionCalculator
{
    /// <summary>
    /// Shared singleton. The convention is stateless so a single instance is safe for
    /// concurrent use across the process.
    /// </summary>
    public static readonly Thirty360 Instance = new();

    private Thirty360() { }

    /// <inheritdoc />
    public string Code => "30/360";

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

        // ISDA Bond Basis: always cap D1 to 30. Cap D2 to 30 only when the capped D1 is 30
        // (which happens when original D1 was 30 or 31) AND D2 is 31 — that is the signature
        // Bond Basis rule that keeps Jan 15 → Jul 31 at 196/360 rather than collapsing it to
        // the 30E/360 answer of 195/360.
        var d1 = Math.Min(startDate.Day, 30);
        var d2 = (d1 == 30 && endDate.Day == 31) ? 30 : endDate.Day;

        var years = endDate.Year - startDate.Year;
        var months = endDate.Month - startDate.Month;
        var days = d2 - d1;

        return ((360 * years) + (30 * months) + days) / 360.0;
    }
}
