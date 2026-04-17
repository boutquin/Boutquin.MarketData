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
/// Strongly-typed identifier for an exchange futures-product code (e.g. <c>"ES"</c>
/// for E-mini S&amp;P 500, <c>"SR3"</c> for 3-month SOFR futures, <c>"ZQ"</c> for
/// Fed Funds futures).
/// </summary>
/// <remarks>
/// <para>
/// Replaces the bare <c>string ProductCode</c> on
/// <see cref="Requests.FuturesSettlementRequest"/> and
/// <see cref="Records.FuturesSettlement.ContractCode"/>. Casing is preserved; callers
/// wishing to canonicalise to upper-case should do so at the adapter boundary before
/// constructing the value, because some venues distinguish case-sensitive product codes.
/// </para>
/// </remarks>
public readonly record struct FuturesProductCode : IComparable<FuturesProductCode>
{
    /// <summary>
    /// The futures product / contract root code.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new <see cref="FuturesProductCode"/>.
    /// </summary>
    /// <param name="value">
    /// The product code. Must be non-null and contain at least one non-whitespace
    /// character.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is <see langword="null"/>, empty, or
    /// whitespace.
    /// </exception>
    [JsonConstructor]
    public FuturesProductCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    /// <summary>
    /// Compares two product codes using ordinal string ordering on <see cref="Value"/>.
    /// </summary>
    /// <param name="other">The product code to compare against.</param>
    /// <returns>Ordinal comparison on <see cref="Value"/>.</returns>
    public int CompareTo(FuturesProductCode other) =>
        string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}
