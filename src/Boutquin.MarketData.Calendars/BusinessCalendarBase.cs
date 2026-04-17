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

using Boutquin.MarketData.Abstractions.Calendars;

namespace Boutquin.MarketData.Calendars;

/// <summary>
/// Base class for business calendars. Provides concrete implementations of
/// <see cref="IBusinessCalendar.Adjust"/> and <see cref="IBusinessCalendar.Advance"/>
/// that delegate to the abstract <see cref="IsBusinessDay"/> predicate.
/// </summary>
/// <remarks>
/// Subclasses need only implement <see cref="Code"/>, <see cref="BusinessDaysPerYear"/>,
/// and <see cref="IsBusinessDay"/>. All date-rolling and advancement logic is shared.
/// </remarks>
public abstract class BusinessCalendarBase : IBusinessCalendar
{
    /// <inheritdoc />
    public abstract string Code { get; }

    /// <inheritdoc />
    public abstract int BusinessDaysPerYear { get; }

    /// <inheritdoc />
    public abstract bool IsBusinessDay(DateOnly date);

    /// <inheritdoc />
    public DateOnly Adjust(DateOnly date, BusinessDayAdjustment adjustment) =>
        adjustment switch
        {
            BusinessDayAdjustment.None => date,
            BusinessDayAdjustment.Following => RollForward(date),
            BusinessDayAdjustment.ModifiedFollowing => ModifiedRollForward(date),
            BusinessDayAdjustment.Preceding => RollBackward(date),
            BusinessDayAdjustment.ModifiedPreceding => ModifiedRollBackward(date),
            _ => throw new ArgumentOutOfRangeException(nameof(adjustment), adjustment, "Unknown adjustment mode."),
        };

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="businessDays"/> equals <see cref="int.MinValue"/>,
    /// because the counter cannot represent the positive magnitude.
    /// </exception>
    public DateOnly Advance(DateOnly date, int businessDays)
    {
        // Math.Abs(int.MinValue) throws OverflowException because +2^31 does not fit in int.
        // Guard explicitly so callers get a domain-appropriate exception rather than a
        // confusing arithmetic overflow.
        if (businessDays == int.MinValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(businessDays),
                $"businessDays cannot equal int.MinValue ({int.MinValue}); use a larger value.");
        }

        var step = businessDays >= 0 ? 1 : -1;
        var remaining = Math.Abs(businessDays);
        var current = date;

        while (remaining > 0)
        {
            current = current.AddDays(step);
            if (IsBusinessDay(current))
            {
                remaining--;
            }
        }

        return current;
    }

    private DateOnly RollForward(DateOnly date)
    {
        while (!IsBusinessDay(date))
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private DateOnly RollBackward(DateOnly date)
    {
        while (!IsBusinessDay(date))
        {
            date = date.AddDays(-1);
        }

        return date;
    }

    private DateOnly ModifiedRollForward(DateOnly date)
    {
        var adjusted = RollForward(date);
        if (adjusted.Month != date.Month)
        {
            adjusted = RollBackward(date);
        }

        return adjusted;
    }

    private DateOnly ModifiedRollBackward(DateOnly date)
    {
        var adjusted = RollBackward(date);
        if (adjusted.Month != date.Month)
        {
            adjusted = RollForward(date);
        }

        return adjusted;
    }
}
