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
/// financial center and publication-calendar codes.
/// </summary>
/// <remarks>
/// Unrecognized codes fall back to a <see cref="WeekendOnlyCalendar"/> so callers
/// always receive a usable calendar. Use <see cref="SupportedCodes"/> to enumerate
/// the codes that produce holiday-aware calendars.
/// </remarks>
public static class HolidayCalendarFactory
{
    private static readonly ImmutableArray<string> s_codes =
        ["USNY", "GBLO", "TARGET", "CATO", "XCME", "USSF", "USSOFR", "CAST"];

    /// <summary>
    /// The set of market and publication codes that produce holiday-aware calendars.
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
    /// Creates a calendar for the specified market or publication code.
    /// </summary>
    /// <param name="code">
    /// A financial center code such as <c>"USNY"</c>, <c>"GBLO"</c>, <c>"TARGET"</c>,
    /// <c>"CATO"</c>, <c>"USSF"</c> (SIFMA bond trading), <c>"USSOFR"</c> (SOFR publication),
    /// <c>"CAST"</c> (CA statutory),
    /// <c>"ZURH"</c> (Zurich / CHF SARON, weekend-only fallback), or <c>"TKYO"</c>
    /// (Tokyo / JPY TONA, weekend-only fallback). Unrecognized codes also produce a
    /// weekend-only calendar so benchmark catalogs always resolve.
    /// </param>
    /// <returns>An <see cref="IBusinessCalendar"/> for the requested market or publication schedule.</returns>
    /// <remarks>
    /// USSOFR adds the New York Fed's explicit non-publication dates on Good Friday 2021,
    /// 2023, and 2026 to the SIFMA full-close dates. Other early-close days remain open.
    /// It inherits the underlying dataset's date limits and does not predict future policy exceptions.
    /// </remarks>
    public static IBusinessCalendar Create(string code) =>
        code switch
        {
            "USNY" => new UsNyseCalendar(),
            "CATO" => new CaTsxCalendar(),
            "USSF" => new HolidayCalendar("USSF", HolidayDatasets.UsSifma(), businessDaysPerYear: 252),
            "USSOFR" => CreateSofrPublicationCalendar(),
            "CAST" => new HolidayCalendar("CAST", HolidayDatasets.CaStatutory(), businessDaysPerYear: 250),
            "GBLO" => new HolidayCalendar("GBLO", HolidayDatasets.GbLondon(), businessDaysPerYear: 252),
            "TARGET" => new HolidayCalendar("TARGET", HolidayDatasets.Target(), businessDaysPerYear: 252),
            "XCME" => new HolidayCalendar("XCME", HolidayDatasets.CmeGroup(), businessDaysPerYear: 252),
            // Zurich (CHF SARON) and Tokyo (JPY TONA) ship as weekend-only calendars.
            // Listing them explicitly in the switch preserves that behavior
            // even if the default arm is later changed to throw.
            "ZURH" => new WeekendOnlyCalendar("ZURH"),
            "TKYO" => new WeekendOnlyCalendar("TKYO"),
            _ => new WeekendOnlyCalendar(code),
        };

    private static IBusinessCalendar CreateSofrPublicationCalendar() =>
        new HolidayCalendar("USSOFR",
        [
            .. HolidayDatasets.UsSifma(),
            // SIFMA early trading closes with explicit NY Fed non-publication notices.
            // https://www.newyorkfed.org/markets/opolicy/operating_policy_210303a
            new DateOnly(2021, 4, 2),
            // https://www.newyorkfed.org/markets/opolicy/operating_policy_230308a
            new DateOnly(2023, 4, 7),
            // https://www.newyorkfed.org/markets/opolicy/operating_policy_260312a
            new DateOnly(2026, 4, 3),
        ], businessDaysPerYear: 252);

}
