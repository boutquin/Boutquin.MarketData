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
/// Strongly-typed identifier for an economic time series (e.g. FRED <c>"DGS10"</c>,
/// <c>"GDP"</c>, <c>"CPIAUCSL"</c>).
/// </summary>
/// <remarks>
/// Replaces the bare <c>string SeriesId</c> on
/// <see cref="Requests.EconomicSeriesRequest"/>. Casing is preserved — FRED uses
/// upper-case identifiers, but other economic-data providers use mixed case, and
/// MarketData delegates canonicalisation to the adapter.
/// </remarks>
public readonly record struct EconomicSeriesId : IComparable<EconomicSeriesId>
{
    /// <summary>
    /// The series identifier (provider-specific).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new <see cref="EconomicSeriesId"/>.
    /// </summary>
    /// <param name="value">
    /// The series identifier. Must be non-null and contain at least one non-whitespace
    /// character.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is <see langword="null"/>, empty, or
    /// whitespace.
    /// </exception>
    [JsonConstructor]
    public EconomicSeriesId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>
    /// Compares two identifiers using ordinal string ordering on <see cref="Value"/>.
    /// </summary>
    /// <param name="other">The identifier to compare against.</param>
    /// <returns>Ordinal comparison on <see cref="Value"/>.</returns>
    public int CompareTo(EconomicSeriesId other) =>
        string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}
