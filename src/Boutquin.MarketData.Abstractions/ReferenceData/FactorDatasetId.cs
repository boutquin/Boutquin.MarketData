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
/// Strongly-typed identifier for an academic factor dataset (e.g.
/// <c>"F-F_Research_Data_Factors_daily"</c>, <c>"F-F_Research_Data_5_Factors_2x3_daily"</c>).
/// </summary>
/// <remarks>
/// Replaces the bare <c>string DatasetName</c> on
/// <see cref="Requests.FactorSeriesRequest"/>. The value preserves casing and
/// separators because factor-library dataset names are slug-style strings that serve
/// directly as filenames in the upstream archives (Ken French Data Library, AQR, etc.).
/// </remarks>
public readonly record struct FactorDatasetId : IComparable<FactorDatasetId>
{
    /// <summary>
    /// The factor dataset name (provider-specific).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new <see cref="FactorDatasetId"/>.
    /// </summary>
    /// <param name="value">
    /// The dataset name. Must be non-null and contain at least one non-whitespace
    /// character.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is <see langword="null"/>, empty, or
    /// whitespace.
    /// </exception>
    [JsonConstructor]
    public FactorDatasetId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>
    /// Compares two dataset IDs using ordinal string ordering on <see cref="Value"/>.
    /// </summary>
    /// <param name="other">The dataset ID to compare against.</param>
    /// <returns>Ordinal comparison on <see cref="Value"/>.</returns>
    public int CompareTo(FactorDatasetId other) =>
        string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}
