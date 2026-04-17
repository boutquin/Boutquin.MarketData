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
using Boutquin.MarketData.Abstractions.Records;

namespace Boutquin.MarketData.Normalization.Normalizers;

/// <summary>
/// Normalizes <see cref="FactorObservation"/> records by converting Fama-French percent-form
/// factor returns to decimal form and standardizing factor names to canonical casing.
/// </summary>
/// <remarks>
/// Fama-French CSV files publish returns in percent (e.g., 0.39 means 0.39%). This normalizer
/// divides all factor values by 100. Factor names are mapped to canonical casing where known
/// ("Mkt-RF", "SMB", "HML", "RMW", "CMA", "RF"); unknown names are uppercased.
/// </remarks>
public sealed class FactorObservationNormalizer : INormalizer<FactorObservation, FactorObservation>
{
    private static readonly Dictionary<string, string> s_canonicalNames = new(StringComparer.OrdinalIgnoreCase)
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

    /// <inheritdoc />
    public FactorObservation Normalize(FactorObservation raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var normalized = new Dictionary<string, decimal>(raw.Factors.Count, StringComparer.Ordinal);

        foreach (var (name, value) in raw.Factors)
        {
            var canonicalName = s_canonicalNames.TryGetValue(name, out var mapped)
                ? mapped
                : name.Trim().ToUpperInvariant();

            normalized[canonicalName] = value / 100m;
        }

        return new FactorObservation(raw.Date, normalized);
    }
}
