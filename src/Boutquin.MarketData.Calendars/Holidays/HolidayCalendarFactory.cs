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

using System.Collections.Immutable;

using Boutquin.MarketData.Abstractions.Calendars;

namespace Boutquin.MarketData.Calendars.Holidays;

/// <summary>
/// Factory that creates <see cref="IBusinessCalendar"/> instances for well-known
/// financial center codes.
/// </summary>
/// <remarks>
/// Unrecognized codes fall back to a <see cref="WeekendOnlyCalendar"/> so callers
/// always receive a usable calendar. Use <see cref="SupportedCodes"/> to enumerate
/// the codes that produce holiday-aware calendars.
/// </remarks>
public static class HolidayCalendarFactory
{
    private static readonly ImmutableArray<string> s_codes =
        ["USNY", "GBLO", "TARGET", "CATO", "XCME", "USSF", "CAST"];

    /// <summary>
    /// The set of financial center codes that produce holiday-aware calendars.
    /// </summary>
    /// <remarks>
    /// Codes such as <c>"ZURH"</c> (Zurich / CHF SARON) and <c>"TKYO"</c> (Tokyo / JPY TONA)
    /// are accepted by <see cref="Create"/> but do not (yet) have a registered holiday
    /// dataset — they are served from the weekend-only fallback and therefore are not
    /// listed here. When a dataset lands, add the code to this list without changing
    /// the contract.
    /// </remarks>
    public static IReadOnlyList<string> SupportedCodes => s_codes;

    /// <summary>
    /// Creates a calendar for the specified financial center code.
    /// </summary>
    /// <param name="code">
    /// A financial center code such as <c>"USNY"</c>, <c>"GBLO"</c>, <c>"TARGET"</c>,
    /// <c>"CATO"</c>, <c>"USSF"</c> (SIFMA bond), <c>"CAST"</c> (CA statutory),
    /// <c>"ZURH"</c> (Zurich / CHF SARON, weekend-only fallback), or <c>"TKYO"</c>
    /// (Tokyo / JPY TONA, weekend-only fallback). Unrecognized codes also produce a
    /// weekend-only calendar so benchmark catalogs always resolve.
    /// </param>
    /// <returns>An <see cref="IBusinessCalendar"/> for the requested center.</returns>
    public static IBusinessCalendar Create(string code) =>
        code switch
        {
            "USNY" => new UsNyseCalendar(),
            "CATO" => new CaTsxCalendar(),
            "USSF" => new HolidayCalendar("USSF", HolidayDatasets.UsSifma(), businessDaysPerYear: 252),
            "CAST" => new HolidayCalendar("CAST", HolidayDatasets.CaStatutory(), businessDaysPerYear: 250),
            "GBLO" => new HolidayCalendar("GBLO", HolidayDatasets.GbLondon(), businessDaysPerYear: 252),
            "TARGET" => new HolidayCalendar("TARGET", HolidayDatasets.Target(), businessDaysPerYear: 252),
            "XCME" => new HolidayCalendar("XCME", HolidayDatasets.CmeGroup(), businessDaysPerYear: 252),
            // Zurich (CHF SARON) and Tokyo (JPY TONA) ship as weekend-only for v1 — see spec
            // §2.8. Listing them explicitly in the switch preserves SPEC-correct behaviour
            // even if the default arm is later changed to throw.
            "ZURH" => new WeekendOnlyCalendar("ZURH"),
            "TKYO" => new WeekendOnlyCalendar("TKYO"),
            _ => new WeekendOnlyCalendar(code),
        };
}
