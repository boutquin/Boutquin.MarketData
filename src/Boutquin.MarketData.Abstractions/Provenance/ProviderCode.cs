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

namespace Boutquin.MarketData.Abstractions.Provenance;

/// <summary>
/// Strongly-typed, case-insensitive identifier for a data provider.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the bare <c>string</c> provider keys that used to be scattered across
/// <see cref="Results.DataProvenance"/>, <see cref="Results.FallbackResult{TResponse}"/>,
/// <see cref="Results.FallbackAttempt"/>, and
/// <see cref="Contracts.IDataSourceAdapter{TRequest, TRecord}.ProviderKey"/>. Historically
/// each adapter invented its own casing (<c>"Tiingo"</c>, <c>"tiingo"</c>, <c>"TIINGO"</c>)
/// so fallback-path audit trails and license-compliance reports required manual
/// case-folding; this type normalises at construction.
/// </para>
/// <para>
/// The constructor rejects <see langword="null"/> and whitespace input and folds the
/// incoming text to lower-case via <see cref="string.ToLowerInvariant"/>. Two
/// <see cref="ProviderCode"/> values constructed from different casings therefore
/// compare equal under both structural equality and <see cref="CompareTo"/>.
/// </para>
/// <para>
/// Adapter packages return provider codes from their adapter implementations. Provider identity
/// uses normalized value equality, so registrations, lookups, and provenance agree even when
/// separate <see cref="ProviderCode"/> instances carry the same value. No global registry is
/// maintained in MarketData itself — two adapters that pick the same value collide at audit
/// rather than at build, but this is considered a code-review concern, not a runtime bug.
/// </para>
/// </remarks>
public readonly record struct ProviderCode : IComparable<ProviderCode>
{
    /// <summary>
    /// The canonical lower-case form of the provider key.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new <see cref="ProviderCode"/> from an arbitrary-cased identifier.
    /// </summary>
    /// <param name="value">
    /// The provider key text. Must be non-null and contain at least one non-whitespace
    /// character; casing is normalised to lower-case via
    /// <see cref="string.ToLowerInvariant"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is <see langword="null"/>, empty, or consists
    /// only of whitespace characters.
    /// </exception>
    [JsonConstructor]
    public ProviderCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.ToLowerInvariant();
    }

    /// <summary>
    /// Compares this provider code with another using ordinal string ordering on the
    /// canonical <see cref="Value"/>.
    /// </summary>
    /// <param name="other">The provider code to compare against.</param>
    /// <returns>
    /// A negative value if this code precedes <paramref name="other"/> in ordinal order;
    /// zero if equal; positive if this code follows.
    /// </returns>
    public int CompareTo(ProviderCode other) =>
        string.CompareOrdinal(Value, other.Value);

    /// <summary>
    /// Returns the canonical lower-case provider key.
    /// </summary>
    /// <returns>The value of <see cref="Value"/>, or the empty string for <c>default</c>.</returns>
    public override string ToString() => Value ?? string.Empty;
}
