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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>
/// Requests yield curve quotes (tenor/rate pairs) for a specific curve on a single date.
/// </summary>
/// <param name="CurveId">The curve identifier (e.g., <c>new YieldCurveId("CAD-ZERO")</c>).</param>
/// <param name="AsOfDate">The observation date for the curve snapshot.</param>
public sealed record YieldCurveQuoteRequest(
    YieldCurveId CurveId,
    DateOnly AsOfDate) : IDataRequest
{
    /// <inheritdoc />
    public string DatasetKey => CurveId.Value;

    /// <summary>
    /// Single-day range for the as-of date.
    /// </summary>
    public DateRange Range => new(AsOfDate, AsOfDate);

    /// <summary>
    /// Yield curves are daily observations.
    /// </summary>
    public DataFrequency Frequency => DataFrequency.Daily;
}
