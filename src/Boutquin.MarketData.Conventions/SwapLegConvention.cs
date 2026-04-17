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

using Boutquin.MarketData.Abstractions.Calendars;

namespace Boutquin.MarketData.Conventions;

/// <summary>
/// Represents scheduling and accrual metadata for one leg of an interest-rate swap.
/// </summary>
/// <remarks>
/// Codifies the fixed leg conventions of an interest rate swap: payment frequency, day count,
/// roll convention, and business day adjustment. In a post-LIBOR environment, the floating leg
/// convention is determined by the overnight benchmark and is not separately encoded here,
/// because floating legs are almost universally daily compounded overnight. The fixed leg
/// parameters drive schedule generation and year-fraction calculations that directly affect
/// the par swap rate used in bootstrap calibration.
/// </remarks>
/// <param name="Code">Convention identifier used by curve and trade definitions.</param>
/// <param name="DayCountCode">Day-count convention code used for accrual calculations.</param>
/// <param name="PaymentFrequency">Coupon payment frequency for the leg schedule.</param>
/// <param name="BusinessDayAdjustment">Business-day rule applied to generated schedule dates.</param>
/// <param name="RollConvention">Convention code used for schedule and accrual rules.</param>
/// <param name="PaymentLagBusinessDays">Business-day lag between accrual end and payment date.</param>
/// <param name="EndOfMonth">Whether end-of-month handling is enabled for schedule generation.</param>
/// <param name="CalendarCode">Calendar code for date adjustment (e.g., <c>USNY</c>, <c>GBLO</c>). Defaults to <c>WEEKEND</c> (weekend-only logic) when not specified.</param>
public sealed record SwapLegConvention(
    string Code,
    string DayCountCode,
    PaymentFrequency PaymentFrequency,
    BusinessDayAdjustment BusinessDayAdjustment,
    RollConvention RollConvention,
    int PaymentLagBusinessDays,
    bool EndOfMonth,
    string CalendarCode = "WEEKEND") : IInstrumentConvention;
