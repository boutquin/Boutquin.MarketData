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

namespace Boutquin.MarketData.Calendars;

/// <summary>
/// A calendar that combines weekend rules with an explicit set of holiday dates.
/// A date is a business day only if it is neither a weekend nor a listed holiday.
/// </summary>
/// <remarks>
/// The holiday set is fixed at construction time. For dynamically computed holidays
/// (e.g., Easter-based rules), pre-compute the dates and pass them in, or use an
/// algorithmic calendar such as <see cref="Holidays.UsNyseCalendar"/>.
/// Weekend logic is delegated to <see cref="WeekendOnlyCalendar"/>.
/// </remarks>
public sealed class HolidayCalendar : BusinessCalendarBase
{
    private readonly HashSet<DateOnly> _holidays;
    private readonly WeekendOnlyCalendar _fallback;

    /// <summary>
    /// Initializes a new <see cref="HolidayCalendar"/> with the specified market code and holidays.
    /// </summary>
    /// <param name="code">The market code identifying this calendar (e.g., "USNY", "GBLO").</param>
    /// <param name="holidays">The set of holiday dates to observe.</param>
    /// <param name="businessDaysPerYear">Conventional business days per year for annualization. Defaults to 252.</param>
    public HolidayCalendar(string code, IEnumerable<DateOnly> holidays, int businessDaysPerYear = 252)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(holidays);

        Code = code;
        BusinessDaysPerYear = businessDaysPerYear;
        _holidays = [.. holidays];
        _fallback = new WeekendOnlyCalendar(code, businessDaysPerYear);
    }

    /// <inheritdoc />
    public override string Code { get; }

    /// <inheritdoc />
    public override int BusinessDaysPerYear { get; }

    /// <inheritdoc />
    public override bool IsBusinessDay(DateOnly date) =>
        !_holidays.Contains(date) && _fallback.IsBusinessDay(date);
}
