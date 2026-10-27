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
/// NYSE / NASDAQ equity trading calendar using recurring US market holiday rules
/// and explicitly recorded exceptional closures.
/// </summary>
/// <remarks>
/// Recurring rules use Gregorian Easter from 1583 onward; historical rule changes and
/// exceptional closures are not comprehensively represented. The recorded exceptional
/// closure is January 9, 2025. This is not a complete historical or future exchange schedule.
/// The equity market calendar does <b>not</b> include Columbus Day
/// or Veterans Day, which are SIFMA bond market holidays. For the bond market calendar,
/// use <see cref="HolidayCalendar"/> with <see cref="HolidayDatasets.UsSifma"/>.
/// </remarks>
public sealed class UsNyseCalendar : BusinessCalendarBase
{
    /// <inheritdoc />
    public override string Code => "USNY";

    /// <inheritdoc />
    public override int BusinessDaysPerYear => 252;

    /// <inheritdoc />
    public override bool IsBusinessDay(DateOnly date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        return !IsUsHoliday(date);
    }

    private static bool IsUsHoliday(DateOnly date)
    {
        var year = date.Year;
        var month = date.Month;
        var day = date.Day;

        // NYSE national day of mourning for President Jimmy Carter.
        // https://www.nyse.com/publicdocs/nyse/markets/american-options/rule-interpretations/2025/National_Day_of_Mourning_20250102.pdf
        if (date == new DateOnly(2025, 1, 9))
        {
            return true;
        }

        // New Year's Day (Jan 1) — observed
        if (IsObservedHoliday(date, new DateOnly(year, 1, 1)))
        {
            return true;
        }

        // MLK Day — 3rd Monday of January
        if (month == 1 && date.DayOfWeek == DayOfWeek.Monday && CalendarHelpers.NthDayOfWeek(year, 1, DayOfWeek.Monday, 3) == day)
        {
            return true;
        }

        // Presidents' Day — 3rd Monday of February
        if (month == 2 && date.DayOfWeek == DayOfWeek.Monday && CalendarHelpers.NthDayOfWeek(year, 2, DayOfWeek.Monday, 3) == day)
        {
            return true;
        }

        // Good Friday
        if (date == CalendarHelpers.GoodFriday(year))
        {
            return true;
        }

        // Memorial Day — last Monday of May
        if (month == 5 && date.DayOfWeek == DayOfWeek.Monday && LastDayOfWeek(year, 5, DayOfWeek.Monday) == day)
        {
            return true;
        }

        // Juneteenth (Jun 19) — observed, NYSE holiday since 2022
        if (year >= 2022 && IsObservedHoliday(date, new DateOnly(year, 6, 19)))
        {
            return true;
        }

        // Independence Day (Jul 4) — observed
        if (IsObservedHoliday(date, new DateOnly(year, 7, 4)))
        {
            return true;
        }

        // Labor Day — 1st Monday of September
        if (month == 9 && date.DayOfWeek == DayOfWeek.Monday && CalendarHelpers.NthDayOfWeek(year, 9, DayOfWeek.Monday, 1) == day)
        {
            return true;
        }

        // Thanksgiving — 4th Thursday of November
        if (month == 11 && date.DayOfWeek == DayOfWeek.Thursday && CalendarHelpers.NthDayOfWeek(year, 11, DayOfWeek.Thursday, 4) == day)
        {
            return true;
        }

        // Christmas (Dec 25) — observed
        if (IsObservedHoliday(date, new DateOnly(year, 12, 25)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if <paramref name="date"/> is the observed date for <paramref name="holiday"/>.
    /// Saturday holidays are observed on Friday; Sunday holidays on Monday.
    /// </summary>
    private static bool IsObservedHoliday(DateOnly date, DateOnly holiday)
    {
        var observed = holiday.DayOfWeek switch
        {
            DayOfWeek.Saturday => holiday.AddDays(-1), // Friday
            DayOfWeek.Sunday => holiday.AddDays(1),    // Monday
            _ => holiday,
        };

        return date == observed;
    }

    /// <summary>
    /// Returns the day-of-month for the last occurrence of a given day of week in a month.
    /// </summary>
    private static int LastDayOfWeek(int year, int month, DayOfWeek dayOfWeek)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var last = new DateOnly(year, month, daysInMonth);
        var offset = ((int)last.DayOfWeek - (int)dayOfWeek + 7) % 7;
        return daysInMonth - offset;
    }
}
