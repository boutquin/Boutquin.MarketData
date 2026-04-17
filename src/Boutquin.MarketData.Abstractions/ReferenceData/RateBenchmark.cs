// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

namespace Boutquin.MarketData.Abstractions.ReferenceData;

/// <summary>
/// Structural description of a rate benchmark — identity plus the conventions
/// needed to fetch, accrue, and present its fixings.
/// </summary>
/// <param name="Name">
/// Canonical benchmark identifier (e.g., <c>USD-SOFR</c>). Used as the lookup key
/// in <see cref="StandardBenchmarks"/> and as the dataset key in data pipeline requests.
/// </param>
/// <param name="Currency">
/// ISO-4217 currency the benchmark is quoted in (e.g., <see cref="CurrencyCode.USD"/>
/// for SOFR, <see cref="CurrencyCode.GBP"/> for SONIA).
/// </param>
/// <param name="Kind">
/// Methodology classification. See <see cref="BenchmarkKind"/> for the full taxonomy.
/// Consumers use this to decide accrual models and transition behaviour.
/// </param>
/// <param name="Tenor">
/// Explicit term structure pillar for term benchmarks (e.g., <c>"3M"</c> for
/// CME Term SOFR 3-month). <see langword="null"/> for overnight benchmarks, which
/// carry no explicit tenor.
/// </param>
/// <param name="FixingLagBusinessDays">
/// Number of business days between the observation date and the rate-fixing date.
/// Typically 0 for most overnight RFRs. A positive value means the fixing is
/// determined in advance of the accrual period.
/// </param>
/// <param name="PublicationLagBusinessDays">
/// Number of business days after the observation date before the administrator
/// publishes the fixing. For example, SOFR and ESTR have a lag of 1 (published the
/// following business day); SONIA and SARON publish on the same day (lag 0).
/// </param>
/// <param name="PublicationCalendarCode">
/// Free-form code identifying the publication holiday calendar, resolved via
/// <c>HolidayCalendarFactory.Create</c>. Examples: <c>"USNY"</c>, <c>"GBLO"</c>,
/// <c>"EUTA"</c>. Free-form because the calendar catalogue is an open set.
/// </param>
/// <param name="AccrualDayCountCode">
/// Free-form day-count code for interest accrual calculations, resolved via
/// <c>DayCountResolver.Resolve</c>. Examples: <c>"ACT/360"</c>, <c>"ACT/365F"</c>.
/// Free-form for the same reason as <paramref name="PublicationCalendarCode"/>.
/// </param>
/// <param name="IsCompoundedInArrears">
/// <see langword="true"/> when the standard market convention is to compound the
/// benchmark in arrears over the accrual period (the norm for overnight RFRs used
/// in OIS and FRNs). <see langword="false"/> for simple (flat) accrual or term rates
/// that carry a forward-looking fixing.
/// </param>
/// <remarks>
/// <see cref="RateBenchmark"/> carries only the reference-data attributes that
/// describe <em>what</em> the benchmark is. Curve construction metadata (how to
/// bootstrap it, pillar selection, interpolation) lives in the Curves repo and
/// is composed on top of this record.
/// </remarks>
public sealed record RateBenchmark(
    BenchmarkName Name,
    CurrencyCode Currency,
    BenchmarkKind Kind,
    Tenor? Tenor,
    int FixingLagBusinessDays,
    int PublicationLagBusinessDays,
    string PublicationCalendarCode,
    string AccrualDayCountCode,
    bool IsCompoundedInArrears);
