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
/// Represents settlement-date rules shared across products.
/// </summary>
/// <remarks>
/// Defines the settlement lag and business-day adjustment for trade-to-value-date resolution.
/// Most OIS and swap instruments settle T+2 using the relevant currency's business calendar,
/// though some markets use T+1 or T+0. Settlement conventions affect pillar date resolution
/// during bootstrap, because the effective start date of each calibration instrument depends
/// on the spot lag applied to the valuation date.
/// </remarks>
/// <param name="Code">Convention identifier used in product and market definitions.</param>
/// <param name="SpotLagBusinessDays">Business-day lag from trade date to spot settlement date.</param>
/// <param name="DateAdjustment">Business-day adjustment rule applied to settlement dates.</param>
public sealed record SettlementConvention(
    string Code,
    int SpotLagBusinessDays,
    BusinessDayAdjustment DateAdjustment) : IInstrumentConvention;
