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
namespace Boutquin.MarketData.Catalog.Governance;

/// <summary>
/// Classifies the reliability and governance posture of a data source, driving fallback
/// ordering and production-readiness decisions.
/// </summary>
/// <remarks>
/// The pipeline uses tier ordering to select preferred sources and control fallback.
/// In production mode, cross-tier fallback may be restricted (e.g., Tier 1 only).
/// In research mode, lower-tier sources may substitute when primary sources lack coverage.
/// </remarks>
public enum DataSourceTier
{
    /// <summary>
    /// Official or regulatory-grade source providing free, authoritative data.
    /// Examples: central bank fixings (NY Fed SOFR, BoE SONIA), government bond yields (US Treasury).
    /// Suitable for production use without licensing constraints.
    /// </summary>
    Tier1OfficialPublic = 1,

    /// <summary>
    /// Free but indicative or delayed source, acceptable for research and backtesting.
    /// Examples: FRED historical yields, delayed exchange data, published historical series.
    /// May not meet production freshness or precision requirements.
    /// </summary>
    Tier2FreeIndicative = 2,

    /// <summary>
    /// Commercial or licensed source requiring a paid subscription for access.
    /// Examples: real-time swap quotes, term benchmark rates (Term SOFR), ICE benchmarks.
    /// Redistribution typically restricted; callers must verify license compliance.
    /// </summary>
    Tier3Commercial = 3,
}
