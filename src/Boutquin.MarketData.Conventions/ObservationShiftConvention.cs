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
/// Defines conventions for shifting floating-rate observation dates relative to
/// accrual period boundaries.
/// </summary>
/// <remarks>
/// Relocated verbatim from <c>Boutquin.Curves.Abstractions.Conventions.ObservationShiftConvention</c>.
/// Used by OIS and compounded-in-arrears swap leg schedules.
/// </remarks>
public enum ObservationShiftConvention
{
    /// <summary>
    /// Use unshifted observation dates.
    /// </summary>
    None = 0,

    /// <summary>
    /// Shift each observation date by the configured lag while preserving accrual boundaries.
    /// </summary>
    Shifted = 1,

    /// <summary>
    /// Apply a lookback window to observations before each accrual subperiod.
    /// </summary>
    Lookback = 2,

    /// <summary>
    /// Freeze the final observations over a lockout window near payment date.
    /// </summary>
    Lockout = 3,
}
