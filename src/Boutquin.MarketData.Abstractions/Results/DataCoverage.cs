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
/// Describes the completeness of a data fetch result, comparing requested versus returned data points.
/// </summary>
/// <param name="RequestedPoints">The total number of data points expected based on the request date range and frequency.</param>
/// <param name="ReturnedPoints">The number of data points actually returned by the adapter.</param>
/// <param name="MissingPoints">The number of expected data points that were not available (holidays, gaps, or source outages).</param>
/// <param name="CoverageRatio">The ratio of returned to requested points, ranging from 0.0 (no data) to 1.0 (complete).</param>
/// <remarks>
/// Coverage is computed by the pipeline after normalization. A ratio below 1.0 does not necessarily
/// indicate an error — weekends and holidays produce expected gaps for daily equity data. Consumers
/// should evaluate coverage in the context of the data frequency and asset class.
/// Use <see cref="Status"/> for quick branch decisions (fallback? accept? reject?) and
/// <see cref="CoverageRatio"/> for diagnostics and reporting.
/// </remarks>
public sealed record DataCoverage(
    int RequestedPoints,
    int ReturnedPoints,
    int MissingPoints,
    decimal CoverageRatio)
{
    /// <summary>
    /// Discrete completeness classification derived from <see cref="CoverageRatio"/>.
    /// </summary>
    public CompletenessStatus Status => CoverageRatio switch
    {
        1.0m => CompletenessStatus.Complete,
        0.0m => CompletenessStatus.Missing,
        _ => CompletenessStatus.Partial
    };
}
