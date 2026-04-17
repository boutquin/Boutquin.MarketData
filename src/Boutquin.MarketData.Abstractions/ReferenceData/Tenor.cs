// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

namespace Boutquin.MarketData.Abstractions.ReferenceData;

/// <summary>
/// Strongly-typed wrapper for a tenor code such as <c>"1M"</c>, <c>"3M"</c>, or <c>"1Y"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Tenor codes follow market convention: <c>"ON"</c> (overnight), <c>"1W"</c>, <c>"1M"</c>,
/// <c>"3M"</c>, <c>"6M"</c>, <c>"1Y"</c>, etc. Wrapping the string in a record struct
/// prevents it from being accidentally interchanged with other string-shaped identifiers
/// (calendar codes, day-count codes, benchmark names).
/// </para>
/// <para>
/// A <see langword="null"/> <see cref="Tenor"/> on a <see cref="RateBenchmark"/> signals that
/// the benchmark is overnight (no explicit term structure), as opposed to a term RFR
/// with a specific pillar.
/// </para>
/// </remarks>
/// <param name="Value">The tenor string (e.g., <c>"ON"</c>, <c>"3M"</c>, <c>"2Y"</c>).</param>
public readonly record struct Tenor(string Value)
{
    /// <summary>
    /// Returns the underlying tenor string.
    /// </summary>
    /// <returns>The <see cref="Value"/> string.</returns>
    public override string ToString() => Value ?? string.Empty;
}
