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
/// Currency is strongly typed, so casing normalization is not applicable at this layer.
/// The normalizer passes <c>DividendType Type</c> through unchanged because issuer-declared
/// classification is not a unit or format concern. Adapters own the selection of <c>Type</c>.
/// </remarks>
public sealed class DividendPaymentNormalizer : INormalizer<DividendPayment, DividendPayment>
{
    private readonly int _decimalPlaces;

    /// <summary>
    /// Initializes a new <see cref="DividendPaymentNormalizer"/>.
    /// </summary>
    /// <param name="decimalPlaces">Decimal places for amount rounding, from 0 through 28. Defaults to 4.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="decimalPlaces"/> is outside the range supported by decimal rounding.
    /// </exception>
    public DividendPaymentNormalizer(int decimalPlaces = 4)
    {
        if (decimalPlaces is < 0 or > 28)
        {
            throw new ArgumentOutOfRangeException(
                nameof(decimalPlaces),
                decimalPlaces,
                "Decimal rounding precision must be between 0 and 28, inclusive.");
        }

        _decimalPlaces = decimalPlaces;
    }

    /// <inheritdoc />
    public DividendPayment Normalize(DividendPayment raw)
    {
        NormalizerInputGuard.ThrowIfNull(raw);

        var amount = Math.Max(0m, raw.Amount);
        amount = Math.Round(amount, _decimalPlaces, MidpointRounding.AwayFromZero);

        return new DividendPayment(raw.ExDate, amount, raw.Currency, raw.Type);
    }
}
