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
/// Normalizes <see cref="YieldCurveQuote"/> records by uppercasing tenor labels, trimming
/// whitespace, and converting rates from percent form (|rate| &gt;= 1) to decimal form.
/// </summary>
/// <remarks>
/// Yield sources often publish rates in percent form (e.g., 4.5 for 4.5%). This normalizer
/// uses the heuristic that rates with |value| &gt;= 1 are in percent form and divides by 100.
/// Rates with |value| &lt; 1 are assumed to already be in decimal form.
/// </remarks>
public sealed class YieldCurveQuoteNormalizer : INormalizer<YieldCurveQuote, YieldCurveQuote>
{
    /// <inheritdoc />
    public YieldCurveQuote Normalize(YieldCurveQuote raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var tenor = raw.Tenor.Trim().ToUpperInvariant();

        // Heuristic: |rate| >= 1 → percent form; |rate| < 1 → already decimal. This covers the
        // overwhelming majority of published yield data but can misclassify legitimately large
        // negative-rate decimals or legitimately small percent values — consumers operating in
        // regimes where that matters (Turkey 2022-style high rates expressed as decimal,
        // Japanese sub-1% negative-rate quotes) should pre-normalize at the adapter and bypass
        // this normalizer for those feeds.
        var rate = Math.Abs(raw.Rate) >= 1m
            ? raw.Rate / 100m
            : raw.Rate;

        return new YieldCurveQuote(tenor, rate);
    }
}
