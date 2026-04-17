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

namespace Boutquin.MarketData.Abstractions.Calendars;

/// <summary>
/// Specifies how a non-business day is rolled to the nearest valid business day.
/// </summary>
/// <remarks>
/// These conventions are standard across fixed-income, FX, and derivatives markets.
/// The "Modified" variants prevent the adjusted date from crossing a month boundary,
/// which would change the accrual period.
/// </remarks>
public enum BusinessDayAdjustment
{
    /// <summary>
    /// No adjustment — return the date as-is even if it falls on a non-business day.
    /// </summary>
    None = 0,

    /// <summary>
    /// Roll forward to the next business day.
    /// </summary>
    Following = 1,

    /// <summary>
    /// Roll forward to the next business day unless that crosses a month boundary,
    /// in which case roll backward instead.
    /// </summary>
    ModifiedFollowing = 2,

    /// <summary>
    /// Roll backward to the previous business day.
    /// </summary>
    Preceding = 3,

    /// <summary>
    /// Roll backward to the previous business day unless that crosses a month boundary,
    /// in which case roll forward instead.
    /// </summary>
    ModifiedPreceding = 4,
}
