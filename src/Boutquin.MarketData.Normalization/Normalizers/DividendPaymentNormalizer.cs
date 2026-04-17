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
/// Normalizes <see cref="DividendPayment"/> records by clamping negative amounts to
/// zero and rounding to a configurable precision.
/// </summary>
/// <remarks>
/// Currency is strongly-typed (<c>CurrencyCode</c>) after the Tier 1 reference-data
/// consolidation — casing normalization is no longer applicable at this layer. The
/// Tier 2 taxonomies spec added a <c>DividendType Type</c> field; the normalizer
/// passes it through unchanged because issuer-declared classification is not a unit
/// or format concern. Adapters own the selection of <c>Type</c>.
/// </remarks>
public sealed class DividendPaymentNormalizer : INormalizer<DividendPayment, DividendPayment>
{
    private readonly int _decimalPlaces;

    /// <summary>
    /// Initializes a new <see cref="DividendPaymentNormalizer"/>.
    /// </summary>
    /// <param name="decimalPlaces">Decimal places for amount rounding. Defaults to 4.</param>
    public DividendPaymentNormalizer(int decimalPlaces = 4)
    {
        _decimalPlaces = decimalPlaces;
    }

    /// <inheritdoc />
    public DividendPayment Normalize(DividendPayment raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var amount = Math.Max(0m, raw.Amount);
        amount = Math.Round(amount, _decimalPlaces, MidpointRounding.AwayFromZero);

        return new DividendPayment(raw.ExDate, amount, raw.Currency, raw.Type);
    }
}
