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
/// Represents convention metadata required to schedule and settle FRA instruments.
/// </summary>
/// <remarks>
/// Codifies Forward Rate Agreement (FRA) instrument conventions: the day count used for
/// rate accrual, settlement lag, and the business-day adjustment rule for fixing and
/// settlement dates. FRA conventions vary by currency and market; the <see cref="Code"/>
/// maps to specific parameters in the <see cref="InstrumentConventionRegistry"/>. FRAs are
/// predominantly a pre-LIBOR cessation instrument, but remain relevant for certain
/// currency curves and legacy portfolio valuation.
/// </remarks>
/// <param name="Code">Convention identifier used by pricing and calibration inputs.</param>
/// <param name="CalendarCode">Calendar code used to adjust FRA start and end dates.</param>
/// <param name="DayCountCode">Day-count convention code used for accrual calculations.</param>
/// <param name="BusinessDayAdjustment">Business-day rule applied to generated FRA dates.</param>
/// <param name="SettlementLagBusinessDays">Business-day lag from trade date to FRA settlement date.</param>
public sealed record FraConvention(
    string Code,
    string CalendarCode,
    string DayCountCode,
    BusinessDayAdjustment BusinessDayAdjustment,
    int SettlementLagBusinessDays) : IInstrumentConvention;
