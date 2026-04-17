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

namespace Boutquin.MarketData.Abstractions.Results;

/// <summary>
/// Discrete completeness classification derived from <see cref="DataCoverage.CoverageRatio"/>,
/// providing a quick branch point for fallback and quality-gate decisions.
/// </summary>
/// <remarks>
/// Use the enum for control flow (e.g., "should I try a fallback source?") and the
/// underlying <see cref="DataCoverage.CoverageRatio"/> for diagnostics and reporting.
/// </remarks>
public enum CompletenessStatus
{
    /// <summary>
    /// All requested data points were returned (<see cref="DataCoverage.CoverageRatio"/> = 1.0).
    /// </summary>
    Complete,

    /// <summary>
    /// Some but not all requested data points were returned (0 &lt; ratio &lt; 1.0).
    /// Expected for weekends, holidays, or partial source coverage.
    /// </summary>
    Partial,

    /// <summary>
    /// No data points were returned (<see cref="DataCoverage.CoverageRatio"/> = 0.0).
    /// May indicate a source outage, invalid dataset key, or empty date range.
    /// </summary>
    Missing
}
