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

using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.Storage.Domain.Attributes;

namespace Boutquin.MarketData.Abstractions.Keys;

/// <summary>
/// Composite partition key for a single academic factor's series:
/// <c>(Provider, DatasetId, Frequency, FactorName)</c>. Example:
/// <c>("famafrench", "F-F_Research_Data_Factors", Daily, "Mkt-RF")</c>.
/// </summary>
/// <param name="Provider">Adapter-assigned provider identifier. Stays <c>string</c> (open registry; wrappers illegal in keys, BSSG014).</param>
/// <param name="DatasetId">Provider-scoped factor-set identifier (e.g. <c>"F-F_Research_Data_Factors"</c>).</param>
/// <param name="Frequency">Observation frequency as the <see cref="DataFrequency"/> enum.</param>
/// <param name="FactorName">The per-factor discriminant (e.g. <c>"Mkt-RF"</c>, <c>"SMB"</c>, <c>"HML"</c>).</param>
/// <remarks>
/// <para><b>FactorName in the key:</b> <see cref="Records.FactorObservation"/>
/// stores one row per <c>(Date, FactorName, Value)</c>, so the
/// store's composite LSM key <c>(DatasetKey, Date)</c> would collide across factors of one date
/// unless <c>FactorName</c> is part of the dataset key. Hoisting it here makes each factor its own
/// series: a per-factor history is a <c>(Provider, DatasetId, Frequency, FactorName)</c> prefix
/// scan, and a full factor-set-as-of-date read is a <c>(Provider, DatasetId, Frequency, *)</c>
/// range filtered to one <c>Date</c>.</para>
/// <para><b>Discriminant identity.</b> <c>FactorName</c> is simultaneously a record body
/// column and this key's discriminant, and normalization rewrites that column. If the key producer
/// and the body producer canonicalised the name differently — or one canonicalised and the other did
/// not — one logical factor would fork into two persisted series that the discriminant-blind
/// membership filter then merges. <see cref="CanonicalFactorName"/> is the single owner of that
/// identity relation: the fan-out applies it to every projected key regardless of whether any
/// normalizer is registered, and <c>FactorObservationNormalizer</c> applies the same function to the
/// body, so the projected key is invariant under normalization.</para>
/// <para>See <see cref="PriceBarDatasetKey"/> remarks for the Storage 2.0 tagged-ordered-wire
/// design (native enums in keys, <c>string</c> provider) and the lexicographic ordering guarantee.</para>
/// </remarks>
[Key]
public readonly partial record struct FactorObservationDatasetKey(
    [property: Tag(1)] string Provider,
    [property: Tag(2)] string DatasetId,
    [property: Tag(3)] DataFrequency Frequency,
    [property: Tag(4)] string FactorName)
{
    private static readonly Dictionary<string, string> s_canonicalFactorNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Mkt-RF"] = "Mkt-RF",
            ["SMB"] = "SMB",
            ["HML"] = "HML",
            ["RMW"] = "RMW",
            ["CMA"] = "CMA",
            ["RF"] = "RF",
            ["Mom"] = "Mom",
            ["WML"] = "WML",
        };

    /// <summary>
    /// The canonical spelling of a factor name — the identity relation for the
    /// <see cref="FactorName"/> discriminant column.
    /// </summary>
    /// <param name="factorName">The raw factor name as published by the source.</param>
    /// <returns>
    /// The academic-canonical spelling for a known factor (<c>"Mkt-RF"</c>, <c>"SMB"</c>,
    /// <c>"HML"</c>, <c>"RMW"</c>, <c>"CMA"</c>, <c>"RF"</c>, <c>"Mom"</c>, <c>"WML"</c>), matched
    /// case-insensitively after trimming; otherwise the trimmed name uppercased.
    /// </returns>
    /// <remarks>
    /// Idempotent — <c>CanonicalFactorName(CanonicalFactorName(x)) == CanonicalFactorName(x)</c> —
    /// which is what makes a key projected through it invariant under normalization.
    /// </remarks>
    /// <exception cref="NullReferenceException">
    /// <paramref name="factorName"/> is <see langword="null"/>. Canonical records carry non-nullable
    /// string columns; a null column is a producer defect, surfaced rather than persisted.
    /// </exception>
    public static string CanonicalFactorName(string factorName)
    {
        var trimmed = factorName.Trim();
        return s_canonicalFactorNames.TryGetValue(trimmed, out var mapped)
            ? mapped
            : trimmed.ToUpperInvariant();
    }
}
