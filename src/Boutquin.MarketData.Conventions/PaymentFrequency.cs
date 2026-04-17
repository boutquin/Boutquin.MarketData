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
/// Defines accrual or payment interval frequencies used by instrument schedules.
/// </summary>
/// <remarks>
/// Relocated verbatim from <c>Boutquin.Curves.Abstractions.Conventions.PaymentFrequency</c>.
/// Member ordinals are preserved so any code that round-trips through an
/// <see cref="int"/> cast continues to produce identical results.
/// </remarks>
public enum PaymentFrequency
{
    /// <summary>
    /// Daily frequency.
    /// </summary>
    Daily = 0,

    /// <summary>
    /// Weekly frequency.
    /// </summary>
    Weekly = 1,

    /// <summary>
    /// Monthly frequency.
    /// </summary>
    Monthly = 2,

    /// <summary>
    /// Quarterly frequency.
    /// </summary>
    Quarterly = 3,

    /// <summary>
    /// Semi-annual frequency.
    /// </summary>
    SemiAnnual = 4,

    /// <summary>
    /// Annual frequency.
    /// </summary>
    Annual = 5,

    /// <summary>
    /// Single-period schedule over the full instrument term.
    /// </summary>
    Term = 6,
}
