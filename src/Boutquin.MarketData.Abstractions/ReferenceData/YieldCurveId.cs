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

using System.Text.Json.Serialization;

namespace Boutquin.MarketData.Abstractions.ReferenceData;

/// <summary>
/// Strongly-typed identifier for a yield curve (e.g. <c>"CAD-ZERO"</c>, <c>"USD-TREASURY"</c>).
/// </summary>
/// <remarks>
/// <para>
/// Replaces the bare <c>string CurveId</c> on
/// <see cref="Requests.YieldCurveQuoteRequest"/> so that a curve identifier cannot be
/// confused with another external identifier at the call site. The wrapper preserves
/// casing and whitespace of the input beyond the null/empty guard — curve naming is
/// an open set owned by the provider (Bank of Canada, Fed H.15, etc.), and MarketData
/// does not impose a canonical form beyond rejecting whitespace.
/// </para>
/// </remarks>
public readonly record struct YieldCurveId : IComparable<YieldCurveId>
{
    /// <summary>
    /// The curve identifier text (provider-specific).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new <see cref="YieldCurveId"/>.
    /// </summary>
    /// <param name="value">
    /// The curve identifier. Must be non-null and contain at least one non-whitespace
    /// character.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is <see langword="null"/>, empty, or
    /// whitespace.
    /// </exception>
    [JsonConstructor]
    public YieldCurveId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>
    /// Compares two identifiers using ordinal string ordering on <see cref="Value"/>.
    /// </summary>
    /// <param name="other">The identifier to compare against.</param>
    /// <returns>
    /// A negative value if this identifier precedes <paramref name="other"/> in
    /// ordinal order; zero if equal; positive if this identifier follows.
    /// </returns>
    public int CompareTo(YieldCurveId other) =>
        string.CompareOrdinal(Value, other.Value);

    /// <summary>
    /// Returns the underlying identifier text.
    /// </summary>
    /// <returns>The value of <see cref="Value"/>, or the empty string for <c>default</c>.</returns>
    public override string ToString() => Value ?? string.Empty;
}
