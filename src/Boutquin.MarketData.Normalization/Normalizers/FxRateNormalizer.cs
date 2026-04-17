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
/// Normalizes <see cref="FxRate"/> records by rounding the rate to a configurable
/// precision.
/// </summary>
/// <remarks>
/// Currency codes are strongly-typed (<c>CurrencyCode</c>) after the reference-data
/// consolidation spec; casing normalization is no longer applicable at this layer.
/// </remarks>
public sealed class FxRateNormalizer : INormalizer<FxRate, FxRate>
{
    private readonly int _decimalPlaces;

    /// <summary>
    /// Initializes a new <see cref="FxRateNormalizer"/>.
    /// </summary>
    /// <param name="decimalPlaces">The number of decimal places for rate rounding. Defaults to 6.</param>
    public FxRateNormalizer(int decimalPlaces = 6)
    {
        _decimalPlaces = decimalPlaces;
    }

    /// <inheritdoc />
    public FxRate Normalize(FxRate raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        return new FxRate(
            raw.Date,
            raw.BaseCurrency,
            raw.QuoteCurrency,
            Math.Round(raw.Rate, _decimalPlaces, MidpointRounding.AwayFromZero));
    }
}
