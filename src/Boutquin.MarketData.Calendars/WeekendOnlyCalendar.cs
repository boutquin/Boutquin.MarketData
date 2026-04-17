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
/// A calendar that treats only weekends (Saturday and Sunday) as non-business days.
/// No holidays are recognized.
/// </summary>
/// <remarks>
/// Useful as a base calendar or fallback when no holiday schedule is available.
/// Also serves as the foundation for <see cref="HolidayCalendar"/>, which layers
/// specific holiday dates on top of weekend rules.
/// </remarks>
public sealed class WeekendOnlyCalendar : BusinessCalendarBase
{
    /// <summary>
    /// Initializes a new <see cref="WeekendOnlyCalendar"/> with the specified market code.
    /// </summary>
    /// <param name="code">
    /// The market code identifying this calendar. Defaults to <c>"WEEKEND"</c>.
    /// </param>
    /// <param name="businessDaysPerYear">
    /// Conventional business days per year for annualization. Defaults to 260 (52 weeks x 5 days).
    /// </param>
    public WeekendOnlyCalendar(string code = "WEEKEND", int businessDaysPerYear = 260)
    {
        Code = code;
        BusinessDaysPerYear = businessDaysPerYear;
    }

    /// <inheritdoc />
    public override string Code { get; }

    /// <inheritdoc />
    public override int BusinessDaysPerYear { get; }

    /// <inheritdoc />
    public override bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
}
