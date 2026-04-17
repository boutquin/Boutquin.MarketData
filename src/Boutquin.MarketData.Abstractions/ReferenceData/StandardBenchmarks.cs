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
/// Canonical catalogue of the six major overnight rate benchmarks referenced by
/// Hull 11e Ch 4: USD SOFR, GBP SONIA, EUR ESTR, CAD CORRA, CHF SARON, JPY TONA.
/// </summary>
/// <remarks>
/// <para>
/// The catalogue is a <em>starting point</em> — consumers may construct
/// <see cref="RateBenchmark"/> values directly for any benchmark not listed here.
/// The listed conventions follow the administrators' published methodologies
/// (ARRC for SOFR, WMBA for SONIA, ECB for ESTR, BoC for CORRA, SIX for SARON,
/// and BOJ for TONA). Each benchmark is an overnight risk-free or
/// overnight-indexed rate. All six are compounded in arrears per standard
/// OIS and FRN market conventions.
/// </para>
/// </remarks>
public static class StandardBenchmarks
{
    /// <summary>
    /// USD SOFR — Secured Overnight Financing Rate (ACT/360, USNY, fixing lag 0, publication lag 1, compounded in arrears).
    /// </summary>
    public static RateBenchmark UsdSofr { get; } = new(
        new BenchmarkName("USD-SOFR"),
        CurrencyCode.USD,
        BenchmarkKind.OvernightRiskFree,
        Tenor: null,
        FixingLagBusinessDays: 0,
        PublicationLagBusinessDays: 1,
        PublicationCalendarCode: "USNY",
        AccrualDayCountCode: "ACT/360",
        IsCompoundedInArrears: true);

    /// <summary>
    /// GBP SONIA — Sterling Overnight Index Average (ACT/365F, GBLO, fixing lag 0, publication lag 0, compounded in arrears).
    /// </summary>
    public static RateBenchmark GbpSonia { get; } = new(
        new BenchmarkName("GBP-SONIA"),
        CurrencyCode.GBP,
        BenchmarkKind.OvernightRiskFree,
        Tenor: null,
        FixingLagBusinessDays: 0,
        PublicationLagBusinessDays: 0,
        PublicationCalendarCode: "GBLO",
        AccrualDayCountCode: "ACT/365F",
        IsCompoundedInArrears: true);

    /// <summary>
    /// EUR ESTR — Euro Short-Term Rate (ACT/360, EUTA, fixing lag 0, publication lag 1, compounded in arrears).
    /// </summary>
    public static RateBenchmark EurEstr { get; } = new(
        new BenchmarkName("EUR-ESTR"),
        CurrencyCode.EUR,
        BenchmarkKind.OvernightRiskFree,
        Tenor: null,
        FixingLagBusinessDays: 0,
        PublicationLagBusinessDays: 1,
        PublicationCalendarCode: "EUTA",
        AccrualDayCountCode: "ACT/360",
        IsCompoundedInArrears: true);

    /// <summary>
    /// CAD CORRA — Canadian Overnight Repo Rate Average (ACT/365F, CATO, fixing lag 0, publication lag 1, compounded in arrears).
    /// </summary>
    public static RateBenchmark CadCorra { get; } = new(
        new BenchmarkName("CAD-CORRA"),
        CurrencyCode.CAD,
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
        CurrencyCode.CHF,
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
        CurrencyCode.JPY,
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
