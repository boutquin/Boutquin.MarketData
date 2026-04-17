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

namespace Boutquin.MarketData.DayCount;

/// <summary>
/// Computes the year fraction between two dates according to a day-count convention.
/// </summary>
/// <remarks>
/// Day-count conventions determine how time is measured for accrual, discounting,
/// and rate conversions. Different markets use different conventions — the interface
/// abstracts this so callers can swap conventions without changing computation logic.
/// </remarks>
public interface IYearFractionCalculator
{
    /// <summary>
    /// The standard market code for this convention (e.g., "ACT/360", "ACT/365F", "30/360").
    /// </summary>
    string Code { get; }

    /// <summary>
    /// Computes the year fraction between two dates.
    /// </summary>
    /// <param name="startDate">The period start date (inclusive).</param>
    /// <param name="endDate">The period end date (exclusive).</param>
    /// <returns>The year fraction as a double.</returns>
    double YearFraction(DateOnly startDate, DateOnly endDate);
}
