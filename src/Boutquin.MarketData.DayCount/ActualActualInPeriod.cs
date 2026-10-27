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
/// ACT/ACT (in-period) day-count convention — also known as ACT/ACT ICMA, ACT/ACT Bond.
/// Standard basis for US Treasury bonds, euro/sterling government bonds, and many
/// coupon-bearing bond instruments.
/// </summary>
/// <remarks>
/// <para>
/// Reference: ICMA Rule 251 — for a coupon period running from <c>t₀</c> to <c>t₁</c>
/// with coupon frequency <c>m</c>, the year fraction from <c>t₀</c> to <c>t</c> is
/// <c>(t − t₀) / (m · (t₁ − t₀))</c>. A half-period accrues exactly <c>1/(2m)</c>; a
/// full period accrues exactly <c>1/m</c>. Callers must split spans across multiple coupon
/// periods and sum the per-period fractions. Each call requires dates within its supplied
/// <c>(periodStart, periodEnd)</c>; this implementation does not split schedules.
/// </para>
/// <para>
/// <strong>Fallback behaviour.</strong> Callers that do not know the coupon schedule
/// (the 2-argument overload) get a schedule-free approximation: days / 365.25.
/// This does not implement the coupon-aware ICMA convention and is not a substitute
/// for the coupon-aware overload when the accrual schedule matters.
/// </para>
/// <para>
/// Conventions used by this implementation:
/// </para>
/// <list type="bullet">
///   <item><description>Coupon frequency <c>m</c> is interpreted as "coupons per year" — 2 for semiannual, 1 for annual, 4 for quarterly.</description></item>
///   <item><description>When the interval is contained within a single coupon period, year-fraction collapses to the ICMA Rule 251 per-period formula.</description></item>
///   <item><description>When the interval spans the full coupon period exactly, the result is <c>1/m</c> by construction.</description></item>
/// </list>
/// </remarks>
public sealed class ActualActualInPeriod : IYearFractionCalculator
{
    /// <summary>
    /// Shared singleton. The convention is stateless so a single instance is safe for
    /// concurrent use across the process.
    /// </summary>
    public static readonly ActualActualInPeriod Instance = new();

    private ActualActualInPeriod() { }

    /// <inheritdoc />
    public string Code => "ACT/ACT";

    /// <inheritdoc />
    /// <remarks>
    /// Schedule-free ACT/365.25 approximation for callers without coupon context.
    /// This is not the ICMA convention. Use the five-argument overload for each
    /// regular or notional coupon period and sum the fractions across periods.
    /// </remarks>
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

        var days = endDate.DayNumber - startDate.DayNumber;
        return days / 365.25;
    }

    /// <summary>
    /// Computes the ICMA Rule 251 year fraction for an interval that is known to lie
    /// in a specific coupon period.
    /// </summary>
    /// <param name="start">Interval start (inclusive).</param>
    /// <param name="end">Interval end (exclusive).</param>
    /// <param name="periodStart">Coupon period start (inclusive).</param>
    /// <param name="periodEnd">Coupon period end (exclusive).</param>
    /// <param name="couponFrequency">Coupons per year (2 = semiannual, 1 = annual, 4 = quarterly).</param>
    /// <returns>
    /// <c>(end − start) / (couponFrequency · (periodEnd − periodStart))</c>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// When <paramref name="couponFrequency"/> is not positive, or when <paramref name="end"/>
    /// is earlier than <paramref name="start"/>, <paramref name="start"/> is earlier than
    /// <paramref name="periodStart"/>, or <paramref name="end"/> is later than <paramref name="periodEnd"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// When <paramref name="periodEnd"/> is not strictly later than
    /// <paramref name="periodStart"/>.
    /// </exception>
    public double YearFraction(
        DateOnly start,
        DateOnly end,
        DateOnly periodStart,
        DateOnly periodEnd,
        int couponFrequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(couponFrequency);

        if (end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                $"end ({end:O}) must be on or after start ({start:O}).");
        }

        var periodDays = periodEnd.DayNumber - periodStart.DayNumber;
        if (periodDays <= 0)
        {
            throw new ArgumentException(
                "Coupon periodEnd must be strictly later than periodStart.",
                nameof(periodEnd));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(start, periodStart);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, periodEnd);

        var intervalDays = end.DayNumber - start.DayNumber;
        return intervalDays / ((double)couponFrequency * periodDays);
    }
}
