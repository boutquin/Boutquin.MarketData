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
/// Defines placement and length preference for irregular first or last schedule periods.
/// </summary>
/// <remarks>
/// Relocated verbatim from <c>Boutquin.Curves.Abstractions.Conventions.StubConvention</c>.
/// </remarks>
public enum StubConvention
{
    /// <summary>
    /// No explicit stub preference.
    /// </summary>
    None = 0,

    /// <summary>
    /// Allow a short initial stub period.
    /// </summary>
    ShortInitial = 1,

    /// <summary>
    /// Allow a long initial stub period.
    /// </summary>
    LongInitial = 2,

    /// <summary>
    /// Allow a short final stub period.
    /// </summary>
    ShortFinal = 3,

    /// <summary>
    /// Allow a long final stub period.
    /// </summary>
    LongFinal = 4,
}
