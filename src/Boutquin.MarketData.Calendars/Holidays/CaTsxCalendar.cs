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

namespace Boutquin.MarketData.Calendars.Holidays;

/// <summary>
/// TSX (Toronto Stock Exchange) trading calendar. Computes Canadian market holidays
/// algorithmically for any year, including weekend observance rules.
/// </summary>
/// <remarks>
/// This is the equity exchange calendar (9 holidays). It does <b>not</b> include Truth
/// and Reconciliation Day or Remembrance Day, for which the TSX remains open.
/// For the statutory calendar, use <see cref="HolidayCalendar"/> with
/// <see cref="HolidayDatasets.CaStatutory"/>.
/// </remarks>
public sealed class CaTsxCalendar : BusinessCalendarBase
{
    /// <inheritdoc />
    public override string Code => "CATO";

    /// <inheritdoc />
    public override int BusinessDaysPerYear => 250;

    /// <inheritdoc />
    public override bool IsBusinessDay(DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        return !IsCanadianHoliday(date);
    }

    private static bool IsCanadianHoliday(DateOnly date)
    {
        var year = date.Year;
        var month = date.Month;
        var day = date.Day;

        // New Year's Day (Jan 1) — observed
        if (IsObservedHoliday(date, new DateOnly(year, 1, 1)))
        {
            return true;
        }

        // Family Day — 3rd Monday of February
        if (month == 2 && date.DayOfWeek == DayOfWeek.Monday && CalendarHelpers.NthDayOfWeek(year, 2, DayOfWeek.Monday, 3) == day)
        {
            return true;
        }

        // Good Friday
        if (date == CalendarHelpers.GoodFriday(year))
        {
            return true;
        }

        // Victoria Day — Monday before May 25
        if (month == 5 && date.DayOfWeek == DayOfWeek.Monday)
        {
            var may25 = new DateOnly(year, 5, 25);
            var victoriaDay = may25.DayOfWeek == DayOfWeek.Monday
                ? may25
                : may25.AddDays(-((int)may25.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7);
            if (date == victoriaDay)
            {
                return true;
            }
        }

        // Canada Day (Jul 1) — observed (Saturday→Monday, Sunday→Monday)
        if (IsObservedHoliday(date, new DateOnly(year, 7, 1)))
        {
            return true;
        }

        // Civic Holiday — 1st Monday of August
        if (month == 8 && date.DayOfWeek == DayOfWeek.Monday && CalendarHelpers.NthDayOfWeek(year, 8, DayOfWeek.Monday, 1) == day)
        {
            return true;
        }

        // Labour Day — 1st Monday of September
        if (month == 9 && date.DayOfWeek == DayOfWeek.Monday && CalendarHelpers.NthDayOfWeek(year, 9, DayOfWeek.Monday, 1) == day)
        {
            return true;
        }

        // Canadian Thanksgiving — 2nd Monday of October
        if (month == 10 && date.DayOfWeek == DayOfWeek.Monday && CalendarHelpers.NthDayOfWeek(year, 10, DayOfWeek.Monday, 2) == day)
        {
            return true;
        }

        // Christmas (Dec 25) and Boxing Day (Dec 26) — with special weekend handling
        if (IsChristmasOrBoxingDayObserved(date, year))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles Christmas + Boxing Day observance as a pair.
    /// When both fall on weekend, Monday + Tuesday are observed.
    /// </summary>
    private static bool IsChristmasOrBoxingDayObserved(DateOnly date, int year)
    {
        var christmas = new DateOnly(year, 12, 25);

        // Saturday Christmas: Fri=Christmas observed, Mon=Boxing Day observed
        if (christmas.DayOfWeek == DayOfWeek.Saturday)
        {
            return date == christmas.AddDays(-1) || date == christmas.AddDays(2);
        }

        // Sunday Christmas: Mon=Christmas observed, Tue=Boxing Day observed
        if (christmas.DayOfWeek == DayOfWeek.Sunday)
        {
            return date == christmas.AddDays(1) || date == christmas.AddDays(2);
        }

        // Friday Christmas: Christmas=Fri, Boxing Day=Sat -> Boxing Day observed Mon
        if (christmas.DayOfWeek == DayOfWeek.Friday)
        {
            return date == christmas || date == christmas.AddDays(3);
        }

        // Normal weekday Christmas: both are on weekdays
        var boxingDay = new DateOnly(year, 12, 26);
        return date == christmas || date == boxingDay;
    }

    /// <summary>
    /// Returns true if <paramref name="date"/> is the observed date for <paramref name="holiday"/>.
    /// Saturday holidays are observed on the following Monday; Sunday holidays on Monday.
    /// </summary>
    private static bool IsObservedHoliday(DateOnly date, DateOnly holiday)
    {
        var observed = holiday.DayOfWeek switch
        {
            DayOfWeek.Saturday => holiday.AddDays(2), // Monday
            DayOfWeek.Sunday => holiday.AddDays(1),   // Monday
            _ => holiday,
        };

        return date == observed;
    }
}
