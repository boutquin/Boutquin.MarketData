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

namespace Boutquin.MarketData.Abstractions.Calendars;

/// <summary>
/// Determines whether a given date is a business day and provides date-rolling
/// and advancement operations according to a specific market calendar.
/// </summary>
/// <remarks>
/// Business calendars encode the non-trading days for a particular market or
/// jurisdiction. Implementations range from simple weekend-only rules to full
/// holiday schedules and composite multi-market calendars.
/// </remarks>
public interface IBusinessCalendar
{
    /// <summary>
    /// The standard market code identifying this calendar (e.g., "USNY", "GBLO", "TARGET").
    /// </summary>
    string Code { get; }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="date"/> is a valid business day
    /// according to this calendar.
    /// </summary>
    /// <param name="date">The date to evaluate.</param>
    /// <returns><see langword="true"/> if the date is a business day; otherwise <see langword="false"/>.</returns>
    bool IsBusinessDay(DateOnly date);

    /// <summary>
    /// Adjusts <paramref name="date"/> to a valid business day using the specified
    /// <paramref name="adjustment"/> convention.
    /// </summary>
    /// <param name="date">The date to adjust.</param>
    /// <param name="adjustment">The business-day adjustment rule to apply.</param>
    /// <returns>The adjusted business day.</returns>
    DateOnly Adjust(DateOnly date, BusinessDayAdjustment adjustment);

    /// <summary>
    /// Advances <paramref name="date"/> by <paramref name="businessDays"/> business days.
    /// A positive value moves forward; a negative value moves backward.
    /// </summary>
    /// <param name="date">The starting date.</param>
    /// <param name="businessDays">The signed number of business days to advance.</param>
    /// <returns>The resulting business day.</returns>
    DateOnly Advance(DateOnly date, int businessDays);

    /// <summary>
    /// The conventional number of business days per year for this calendar,
    /// used for annualization of returns, volatility, and risk metrics.
    /// </summary>
    /// <remarks>
    /// Common values: 252 (US equities/NYSE), 250 (TSX), 260 (weekend-only).
    /// Composite calendars derive this from their constituents.
    /// </remarks>
    int BusinessDaysPerYear { get; }

    /// <summary>
    /// Returns all business days in the inclusive range [<paramref name="start"/>, <paramref name="end"/>].
    /// </summary>
    /// <param name="start">The start date (inclusive).</param>
    /// <param name="end">The end date (inclusive).</param>
    /// <returns>An ordered list of business days in the range.</returns>
    IReadOnlyList<DateOnly> BusinessDaysBetween(DateOnly start, DateOnly end)
    {
        var result = new List<DateOnly>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (IsBusinessDay(d))
            {
                result.Add(d);
            }

            if (d == end)
            {
                break;
            }
        }

        return result;
    }
}
