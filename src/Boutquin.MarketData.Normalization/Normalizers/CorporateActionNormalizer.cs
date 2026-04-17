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
/// Normalizes <see cref="CorporateAction"/> records by clamping negative amounts and
/// factors and trimming descriptions.
/// </summary>
/// <remarks>
/// Symbol and Currency are strongly-typed after the reference-data consolidation
/// spec — typed enums cannot be "uppercased" or "trimmed" (invalid characters are
/// already rejected at construction time), so only amount clamping and description
/// trimming remain.
/// </remarks>
public sealed class CorporateActionNormalizer : INormalizer<CorporateAction, CorporateAction>
{
    /// <inheritdoc />
    public CorporateAction Normalize(CorporateAction raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var description = raw.Description?.Trim();

        // Clamp: dividend amounts must be non-negative, split factors must be positive
        var amount = raw.Amount.HasValue ? Math.Max(0m, raw.Amount.Value) : (decimal?)null;
        var factor = raw.Factor.HasValue ? Math.Abs(raw.Factor.Value) : (decimal?)null;

        return new CorporateAction(
            raw.EffectiveDate,
            raw.Symbol,
            raw.ActionType,
            factor,
            amount,
            raw.Currency,
            description);
    }
}
