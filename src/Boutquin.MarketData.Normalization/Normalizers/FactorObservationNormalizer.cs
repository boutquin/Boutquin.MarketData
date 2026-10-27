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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;

namespace Boutquin.MarketData.Normalization.Normalizers;

/// <summary>
/// Normalizes a <see cref="FactorObservation"/> by converting its Fama-French percent-form factor
/// return to decimal form and standardizing the factor name to canonical casing.
/// </summary>
/// <remarks>
/// Fama-French CSV files publish returns in percent (e.g., 0.39 means 0.39%). This normalizer
/// divides the factor value by 100. The factor name is trimmed first — surrounding whitespace never
/// selects a different branch — and then mapped to canonical casing where known ("Mkt-RF", "SMB",
/// "HML", "RMW", "CMA", "RF", "Mom", "WML"); a trimmed name that is not in that set is uppercased.
/// The per-factor canonicalisation rules are unchanged from the prior dictionary-keyed
/// shape — they now apply to the record's single <see cref="FactorObservation.FactorName"/> /
/// <see cref="FactorObservation.Value"/> pair.
/// <para>The name rule itself is <b>not</b> owned here:
/// <see cref="FactorObservation.FactorName"/> is the discriminant column of
/// <see cref="FactorObservationDatasetKey"/>, so the canonical spelling is defined once by
/// <see cref="FactorObservationDatasetKey.CanonicalFactorName"/> and applied both to the projected
/// key (by <c>FactorObservationFanOut</c>, unconditionally) and to the record body here. Encoding
/// that one identity relation in two places is what let the persisted key disagree with the
/// persisted body across a <c>RegisterDefaultNormalizers</c> flip.</para>
/// </remarks>
public sealed class FactorObservationNormalizer : INormalizer<FactorObservation, FactorObservation>
{
    /// <inheritdoc />
    public FactorObservation Normalize(FactorObservation raw)
    {
        NormalizerInputGuard.ThrowIfNull(raw);
        NormalizerInputGuard.ThrowIfRequiredStringIsNull(
            raw.FactorName,
            nameof(FactorObservation.FactorName),
            nameof(raw));

        return new(
            raw.Date,
            FactorObservationDatasetKey.CanonicalFactorName(raw.FactorName),
            raw.Value / 100m);
    }
}
