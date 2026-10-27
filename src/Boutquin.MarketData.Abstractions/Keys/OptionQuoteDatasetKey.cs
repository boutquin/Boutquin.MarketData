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

using Boutquin.MarketData.Abstractions.Records;
using Boutquin.Storage.Domain.Attributes;

namespace Boutquin.MarketData.Abstractions.Keys;

/// <summary>
/// Composite partition key for an option-contract daily-quote series:
/// <c>(Provider, Underlying, Expiry, Strike, Type)</c>. Each option contract is its own time series
/// of daily <see cref="OptionQuote"/> observations.
/// </summary>
/// <param name="Provider">Adapter-assigned provider identifier (e.g. <c>"opra"</c>, <c>"polygon"</c>). Stays <c>string</c> (open registry; wrappers illegal in keys, BSSG014).</param>
/// <param name="Underlying">The underlying ticker — <c>Symbol.Ticker</c> as <c>string</c>, stringified at the builder boundary (wrapper illegal in keys).</param>
/// <param name="Expiry">The option expiration date.</param>
/// <param name="Strike">The strike price. Carries <c>[DecimalScale(4)]</c> — the first (and only) <c>decimal</c> key property in MarketData.</param>
/// <param name="Type">Call or put.</param>
/// <remarks>
/// <para><b>Ordering.</b> Declaration order <c>(Provider, Underlying, Expiry, Strike, Type)</c> makes
/// one contract's daily-quote history a contiguous scan, and a whole option chain for an underlying a
/// <c>(Provider, Underlying, *)</c> range scan (optionally narrowed to one <c>Expiry</c> by extending
/// the prefix) — the volatility-surface read shape. See <see cref="PriceBarDatasetKey"/> remarks for
/// the Storage 2.0 tagged-ordered-wire design and the lexicographic ordering guarantee.</para>
/// <para><b>[DecimalScale(4)] on Strike (BSSG017).</b> <c>Strike</c> is the first <c>decimal</c> key
/// property in MarketData. Ordered-wire encoding must know the canonical scale to preserve the
/// byte-lex ⇔ <c>decimal.Compare</c> invariant across mantissa-equivalent scales, so <c>Strike</c>
/// carries <c>[DecimalScale(4)]</c>: <c>150.0m</c>, <c>150.00m</c>, and <c>150.5m</c> sort by numeric
/// value regardless of the caller's input scale. Scale <c>4</c> is provisional headroom for listed
/// strikes (calibration is Q19); because the scale is baked into the ordered-wire key, changing it is
/// a key-format break.</para>
/// </remarks>
[Key]
public readonly partial record struct OptionQuoteDatasetKey(
    [property: Tag(1)] string Provider,
    [property: Tag(2)] string Underlying,
    [property: Tag(3)] DateOnly Expiry,
    [property: Tag(4), DecimalScale(4)] decimal Strike,
    [property: Tag(5)] OptionType Type);
