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
/// Shared calendar computation helpers used by algorithmic holiday calendar implementations.
/// </summary>
internal static class CalendarHelpers
{
    /// <summary>
    /// Computes Easter Sunday using the Anonymous Gregorian algorithm.
    /// </summary>
    /// <param name="year">
    /// Gregorian year between 1583 (adoption of the Gregorian calendar) and 9999
    /// (<see cref="DateOnly"/> upper bound). Values outside this range produce either
    /// meaningless pre-Gregorian dates or overflow <see cref="DateOnly"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="year"/> is less than 1583 or greater than 9999.
    /// </exception>
    internal static DateOnly ComputeEaster(int year)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1583);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 9999);

        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;

        return new DateOnly(year, month, day);
    }

    /// <summary>
    /// Computes the date of Good Friday for a given year.
    /// </summary>
    internal static DateOnly GoodFriday(int year)
    {
        var easter = ComputeEaster(year);
        return easter.AddDays(-2);
    }

    /// <summary>
    /// Returns the day-of-month for the Nth occurrence of a given day of week in a month.
    /// </summary>
    /// <param name="year">Gregorian year.</param>
    /// <param name="month">Month (1 through 12).</param>
    /// <param name="dayOfWeek">Target day of week.</param>
    /// <param name="n">
    /// Occurrence number within the month (1 through 4, and 5 when the target weekday has a
    /// fifth occurrence).
    /// </param>
    /// <returns>
    /// The day-of-month for the requested occurrence. When <paramref name="n"/> = 5 and the
    /// month has only four occurrences of <paramref name="dayOfWeek"/>, the returned value
    /// exceeds <c>DateTime.DaysInMonth(year, month)</c>; callers must check the result before
    /// constructing a <see cref="DateOnly"/>.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="n"/> is less than 1 or greater than 5.
    /// </exception>
    internal static int NthDayOfWeek(int year, int month, DayOfWeek dayOfWeek, int n)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(n, 5);

        var first = new DateOnly(year, month, 1);
        var offset = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
        return 1 + offset + ((n - 1) * 7);
    }
}
