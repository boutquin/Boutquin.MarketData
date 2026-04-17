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
/// Defines the compounding basis used to quote or convert interest rates.
/// </summary>
/// <remarks>
/// Relocated verbatim from <c>Boutquin.Curves.Abstractions.Conventions.CompoundingConvention</c>.
/// Member ordinals are preserved so any code that round-trips through an
/// <see cref="int"/> cast continues to produce identical results.
/// </remarks>
public enum CompoundingConvention
{
    /// <summary>
    /// Continuous compounding: <c>P(t) = exp(-r·t)</c>.
    /// </summary>
    Continuous = 0,

    /// <summary>
    /// Simple (non-compounded) rate over the accrual period: <c>P(t) = 1/(1 + r·t)</c>.
    /// </summary>
    Simple = 1,

    /// <summary>
    /// Annual compounding: <c>P(t) = 1/(1 + r)^t</c>.
    /// </summary>
    Annual = 2,

    /// <summary>
    /// Semi-annual compounding: <c>P(t) = 1/(1 + r/2)^(2t)</c>.
    /// </summary>
    SemiAnnual = 3,

    /// <summary>
    /// Quarterly compounding: <c>P(t) = 1/(1 + r/4)^(4t)</c>.
    /// </summary>
    Quarterly = 4,

    /// <summary>
    /// Monthly compounding: <c>P(t) = 1/(1 + r/12)^(12t)</c>.
    /// </summary>
    Monthly = 5,
}
