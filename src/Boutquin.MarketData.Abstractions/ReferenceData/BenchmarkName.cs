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
/// Strongly-typed identifier for a rate benchmark (SOFR, SONIA, ESTR, CORRA, SARON, TONA, …).
/// </summary>
/// <remarks>
/// <para>
/// Wrapping the textual benchmark name in a record struct prevents it from being
/// interchanged with other string-shaped identifiers in the system (calendar codes,
/// day-count codes, symbol tickers). The canonical form is an uppercase, hyphenated
/// short name matching market convention — e.g., <c>"USD-SOFR"</c>, <c>"GBP-SONIA"</c>.
/// Producers SHOULD emit the canonical form; lookups that need casing tolerance
/// (such as <see cref="StandardBenchmarks.ByName"/>) are the responsibility of the
/// consumer.
/// </para>
/// </remarks>
/// <param name="Value">Canonical benchmark name.</param>
public readonly record struct BenchmarkName(string Value)
{
    /// <summary>
    /// Returns the underlying identifier value.
    /// </summary>
    /// <returns>The <see cref="Value"/> string.</returns>
    public override string ToString() => Value ?? string.Empty;
}
