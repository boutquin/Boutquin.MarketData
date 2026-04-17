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
/// Represents scheduling, accrual, and observation metadata for OIS instruments.
/// </summary>
/// <remarks>
/// Codifies the market conventions for an Overnight Index Swap (OIS): payment frequency,
/// day count, settlement lag, calendar code, and accrual period adjustment rules. These
/// conventions are referenced by bootstrap node definitions via the <see cref="Code"/>
/// property and resolved through the <see cref="InstrumentConventionRegistry"/> during
/// curve calibration. OIS conventions vary by currency — for example, USD SOFR OIS
/// typically settles T+2 with annual payment frequency, while EUR ESTR OIS uses different
/// lag and frequency parameters.
///
/// OIS conventions are codified in ISDA 2006 Definitions (Section 4) and updated by ISDA
/// supplements for each overnight benchmark. The ARRC (Alternative Reference Rates Committee)
/// publishes recommended conventions for USD SOFR OIS including payment frequency, day count,
/// and settlement lag.
/// </remarks>
/// <param name="Code">Convention identifier used by OIS products and curve nodes.</param>
/// <param name="CalendarCode">Calendar code used to adjust OIS schedule dates.</param>
/// <param name="DayCountCode">Day-count convention code used for accrual calculations.</param>
/// <param name="PaymentFrequency">Coupon payment frequency used by the OIS leg schedule.</param>
/// <param name="BusinessDayAdjustment">Business-day adjustment rule for schedule dates.</param>
/// <param name="SettlementLagBusinessDays">Business-day lag from trade date to OIS start date.</param>
/// <param name="PaymentLagBusinessDays">Business-day lag from accrual end to payment date.</param>
/// <param name="ObservationShiftConvention">Convention code used for schedule and accrual rules.</param>
public sealed record OisConvention(
    string Code,
    string CalendarCode,
    string DayCountCode,
    PaymentFrequency PaymentFrequency,
    BusinessDayAdjustment BusinessDayAdjustment,
    int SettlementLagBusinessDays,
    int PaymentLagBusinessDays,
    ObservationShiftConvention ObservationShiftConvention) : IInstrumentConvention;
