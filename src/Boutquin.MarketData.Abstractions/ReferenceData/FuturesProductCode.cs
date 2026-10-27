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

using Boutquin.Storage.Domain.Attributes;

namespace Boutquin.MarketData.Abstractions.ReferenceData;

/// <summary>
/// Strongly-typed identifier for an exchange futures-product code (e.g. <c>"ES"</c>
/// for E-mini S&amp;P 500, <c>"SR3"</c> for 3-month SOFR futures, <c>"ZQ"</c> for
/// Fed Funds futures).
/// </summary>
/// <param name="Value">
/// The futures product / contract root code. Must be non-null and contain at least one
/// non-whitespace character; validated on construction (including generator round-trip,
/// which reconstructs via this positional constructor).
/// </param>
/// <remarks>
/// <para>
/// Replaces the bare <c>string ProductCode</c> on
/// <see cref="Requests.FuturesSettlementRequest"/> and
/// <see cref="Records.FuturesSettlement.ContractCode"/>. Casing is preserved; callers
/// wishing to canonicalise to upper-case should do so at the adapter boundary before
/// constructing the value, because some venues distinguish case-sensitive product codes.
/// </para>
/// <para>
/// Annotated <c>[StorageSerializable]</c> (positional <c>partial record struct</c> with a
/// tagged constructor parameter) so it round-trips on the Boutquin.Storage 2.0 tagged wire
/// and may be nested inside a serializable record such as
/// <see cref="Records.FuturesSettlement"/>. The Storage generator emits
/// <c>Serialize</c>/<c>Deserialize</c>; deserialization reconstructs through this same
/// validating constructor, so the null/whitespace guard holds on every read path.
/// </para>
/// <para>
/// Initialization and property access enforce the same validation. A default value is invalid
/// for serialization: the generated writer reads the guarded property while staging its payload,
/// before emitting any bytes to the destination stream. The diagnostic <see cref="ToString"/>
/// representation of a default value remains the empty string.
/// </para>
/// </remarks>
[StorageSerializable]
public partial record struct FuturesProductCode(string Value)
    : IComparable<FuturesProductCode>
{
    private string _value = ValidateValue(Value);

    /// <summary>
    /// The futures product / contract root code, validated on initialization and access.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The assigned code is null or whitespace, or an uninitialized default code is accessed.
    /// </exception>
    [Tag(1)]
    public string Value
    {
        get => ValidateValue(_value);
        init => _value = ValidateValue(value);
    }

    private static string ValidateValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }

    /// <summary>
    /// Compares two product codes using ordinal string ordering on <see cref="Value"/>.
    /// </summary>
    /// <param name="other">The product code to compare against.</param>
    /// <returns>Ordinal comparison on <see cref="Value"/>.</returns>
    public int CompareTo(FuturesProductCode other) =>
        string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => _value ?? string.Empty;
}
