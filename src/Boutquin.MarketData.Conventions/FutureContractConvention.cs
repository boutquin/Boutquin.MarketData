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

namespace Boutquin.MarketData.Conventions;

/// <summary>
/// Represents scheduling and accrual metadata for listed futures contracts.
/// </summary>
/// <remarks>
/// Codifies interest rate futures contract conventions: the reference benchmark, day count,
/// contract month cycle (e.g., IMM quarterly dates), and the convexity adjustment lag.
/// Futures prices must be converted to forward rates before use in bootstrap calibration,
/// and the convexity adjustment compensates for the difference between futures-implied
/// rates and true forward rates caused by daily margining.
/// </remarks>
/// <param name="Code">Convention identifier used by future nodes and products.</param>
/// <param name="CalendarCode">Calendar code used for contract and settlement date adjustments.</param>
/// <param name="DayCountCode">Day-count convention code used for accrual calculations.</param>
/// <param name="ContractCycleCode">Cycle identifier such as IMM for contract month generation.</param>
/// <param name="ConvexityAdjustmentLagBusinessDays">Lag used when applying convexity adjustments.</param>
public sealed record FutureContractConvention(
    string Code,
    string CalendarCode,
    string DayCountCode,
    string ContractCycleCode,
    int ConvexityAdjustmentLagBusinessDays = 0) : IInstrumentConvention;
