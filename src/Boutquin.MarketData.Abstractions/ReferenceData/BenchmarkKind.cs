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
/// Classifies the construction methodology of a rate benchmark.
/// </summary>
/// <remarks>
/// <para>
/// Consumers use the kind to decide fallback and transition behaviour (for example,
/// an <see cref="InterbankOffered"/> benchmark implies a LIBOR-era index that should
/// be replaced by a risk-free rate + spread adjustment in new portfolios), and to
/// pick the correct accrual model (overnight-compounded vs. forward-term).
/// </para>
/// </remarks>
public enum BenchmarkKind
{
    /// <summary>
    /// Overnight risk-free rate observed directly in deep repo or unsecured markets
    /// (e.g., SOFR, SONIA, ESTR). No credit-risk component.
    /// </summary>
    OvernightRiskFree = 0,

    /// <summary>
    /// Forward-looking term risk-free benchmark derived from underlying
    /// overnight RFR markets (e.g., CME Term SOFR).
    /// </summary>
    TermRiskFree = 1,

    /// <summary>
    /// Legacy interbank offered rate (the LIBOR family) that embeds a bank
    /// credit-risk component. Being phased out post-2021.
    /// </summary>
    InterbankOffered = 2,

    /// <summary>
    /// Overnight-indexed benchmark published by a central-bank sponsored
    /// administrator (CORRA, TONA, SARON). Behaviourally similar to
    /// <see cref="OvernightRiskFree"/>; distinguished for reporting and
    /// regulatory transparency.
    /// </summary>
    OvernightIndexed = 3,

    /// <summary>
    /// RFR-based fallback-adjusted rate derived by adding a credit adjustment spread
    /// to an overnight risk-free rate. Used as the successor to an IBOR benchmark
    /// under ISDA or regulatory fallback protocols.
    /// </summary>
    FallbackAdjusted = 4,

    /// <summary>
    /// Regulator-maintained synthetic benchmark (e.g., Synthetic GBP LIBOR)
    /// constructed from a term RFR plus a fixed credit adjustment spread.
    /// Intended as a temporary bridge for legacy contracts that cannot be
    /// amended before the cessation date.
    /// </summary>
    Synthetic = 5,

    /// <summary>
    /// Inflation benchmark (CPI / RPI / HICP). Used for inflation-linked
    /// products and real-rate curve construction.
    /// </summary>
    Inflation = 6,

    /// <summary>
    /// Custom or proprietary benchmark not covered by any of the above categories.
    /// </summary>
    Custom = 7,
}
