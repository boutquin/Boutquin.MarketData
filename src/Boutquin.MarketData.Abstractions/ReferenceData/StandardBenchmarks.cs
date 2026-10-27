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

using Boutquin.Domain.Enumerations;

namespace Boutquin.MarketData.Abstractions.ReferenceData;

/// <summary>
/// Catalogue of six overnight rate benchmarks: USD SOFR, GBP SONIA, EUR ESTR,
/// CAD CORRA, CHF SARON, and JPY TONA.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue is a <em>starting point</em> — consumers may construct
/// <see cref="RateBenchmark"/> values directly for any benchmark not listed here.
/// Calendar codes identify the supplied schedules, which have their own coverage limits.
/// In particular, CHZU and JPTO currently resolve to weekend-only fallbacks, while CATO
/// supplies the TSX trading calendar rather than a complete CORRA publication schedule.
/// Consumers needing full administrator-specific history must provide the corresponding
/// calendar. All six entries are classified as overnight risk-free rates and use
/// compounded-in-arrears metadata for OIS and FRN composition.
/// </para>
/// </remarks>
public static class StandardBenchmarks
{
    /// <summary>
    /// USD SOFR — Secured Overnight Financing Rate (ACT/360, USSOFR, fixing lag 0, publication lag 1, compounded in arrears).
    /// </summary>
    /// <remarks>
    /// USSOFR combines SIFMA full-trading closures with explicit New York Fed non-publication
    /// exceptions for Good Friday 2021, 2023, and 2026. SIFMA early closing alone does not
    /// determine publication. See the <see href="https://www.newyorkfed.org/markets/opolicy/operating_policy_260312a">2026 publication notice</see>.
    /// Calendar datasets have documented date limits; future exceptions require updates.
    /// </remarks>
    public static RateBenchmark UsdSofr { get; } = new(
        new BenchmarkName("USD-SOFR"),
        Currency.USD,
        BenchmarkKind.OvernightRiskFree,
        Tenor: null,
        FixingLagBusinessDays: 0,
        PublicationLagBusinessDays: 1,
        PublicationCalendarCode: "USSOFR",
        AccrualDayCountCode: "ACT/360",
        IsCompoundedInArrears: true);

    /// <summary>
    /// GBP SONIA — Sterling Overnight Index Average (ACT/365F, GBLO, fixing lag 0, publication lag 1, compounded in arrears).
    /// </summary>
    /// <remarks>
    /// The Bank of England publishes each observation on the following London business day.
    /// See <see href="https://www.bankofengland.co.uk/markets/sonia-benchmark/sonia-key-features-and-policies">SONIA key features</see>.
    /// </remarks>
    public static RateBenchmark GbpSonia { get; } = new(
        new BenchmarkName("GBP-SONIA"),
        Currency.GBP,
        BenchmarkKind.OvernightRiskFree,
        Tenor: null,
        FixingLagBusinessDays: 0,
        PublicationLagBusinessDays: 1,
        PublicationCalendarCode: "GBLO",
        AccrualDayCountCode: "ACT/365F",
        IsCompoundedInArrears: true);

    /// <summary>
    /// EUR ESTR — Euro Short-Term Rate (ACT/360, TARGET, fixing lag 0, publication lag 1, compounded in arrears).
    /// </summary>
    public static RateBenchmark EurEstr { get; } = new(
        new BenchmarkName("EUR-ESTR"),
        Currency.EUR,
        BenchmarkKind.OvernightRiskFree,
        Tenor: null,
        FixingLagBusinessDays: 0,
        PublicationLagBusinessDays: 1,
        PublicationCalendarCode: "TARGET",
        AccrualDayCountCode: "ACT/360",
        IsCompoundedInArrears: true);

    /// <summary>
    /// CAD CORRA — Canadian Overnight Repo Rate Average (ACT/365F, CATO, fixing lag 0, publication lag 1, compounded in arrears).
    /// </summary>
    public static RateBenchmark CadCorra { get; } = new(
        new BenchmarkName("CAD-CORRA"),
        Currency.CAD,
        BenchmarkKind.OvernightRiskFree,
        Tenor: null,
        FixingLagBusinessDays: 0,
        PublicationLagBusinessDays: 1,
        PublicationCalendarCode: "CATO",
        AccrualDayCountCode: "ACT/365F",
        IsCompoundedInArrears: true);

    /// <summary>
    /// CHF SARON — Swiss Average Rate Overnight (ACT/360, CHZU, fixing lag 0, publication lag 0, compounded in arrears).
    /// </summary>
    /// <remarks>Hull 11e Ch 4 p.99–100.</remarks>
    public static RateBenchmark ChfSaron { get; } = new(
        new BenchmarkName("CHF-SARON"),
        Currency.CHF,
        BenchmarkKind.OvernightRiskFree,
        Tenor: null,
        FixingLagBusinessDays: 0,
        PublicationLagBusinessDays: 0,
        PublicationCalendarCode: "CHZU",
        AccrualDayCountCode: "ACT/360",
        IsCompoundedInArrears: true);

    /// <summary>
    /// JPY TONA — Tokyo Overnight Average Rate (ACT/365F, JPTO, fixing lag 0, publication lag 1, compounded in arrears).
    /// </summary>
    /// <remarks>Hull 11e Ch 4 p.99–100.</remarks>
    public static RateBenchmark JpyTona { get; } = new(
        new BenchmarkName("JPY-TONA"),
        Currency.JPY,
        BenchmarkKind.OvernightRiskFree,
        Tenor: null,
        FixingLagBusinessDays: 0,
        PublicationLagBusinessDays: 1,
        PublicationCalendarCode: "JPTO",
        AccrualDayCountCode: "ACT/365F",
        IsCompoundedInArrears: true);

    private static readonly IReadOnlyList<RateBenchmark> s_all =
    [
        UsdSofr, GbpSonia, EurEstr, CadCorra, ChfSaron, JpyTona,
    ];

    /// <summary>
    /// Returns every benchmark in the catalogue. The returned list is the same
    /// instance on every call; callers may compare by reference.
    /// </summary>
    public static IReadOnlyList<RateBenchmark> All => s_all;

    /// <summary>
    /// Looks up a benchmark by its <see cref="RateBenchmark.Name"/> using
    /// case-insensitive exact match on <see cref="BenchmarkName.Value"/>.
    /// </summary>
    /// <param name="name">The benchmark name to look up (case-insensitive).</param>
    /// <returns>
    /// The matching <see cref="RateBenchmark"/>, or <see langword="null"/> when no
    /// catalogue entry has the requested name. Aliases (e.g., <c>"SOFR"</c> vs.
    /// <c>"USD-SOFR"</c>) are out of scope for v1.
    /// </returns>
    public static RateBenchmark? ByName(string name)
    {
        foreach (var benchmark in s_all)
        {
            if (string.Equals(benchmark.Name.Value, name, StringComparison.OrdinalIgnoreCase))
            {
                return benchmark;
            }
        }

        return null;
    }
}
