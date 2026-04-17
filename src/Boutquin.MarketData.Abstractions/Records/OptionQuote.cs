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

using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// A single option contract quote observation from an exchange or data provider.
/// </summary>
/// <param name="Date">The observation date.</param>
/// <param name="Underlying">The ticker symbol of the underlying security.</param>
/// <param name="Expiry">The option expiration date.</param>
/// <param name="Strike">The strike price.</param>
/// <param name="Type">Call or put.</param>
/// <param name="Bid">The best bid price, or <see langword="null"/> if unavailable.</param>
/// <param name="Ask">The best ask price, or <see langword="null"/> if unavailable.</param>
/// <param name="LastPrice">The last traded price, or <see langword="null"/> if no trades.</param>
/// <param name="ImpliedVolatility">The implied volatility in decimal form (e.g., 0.25 for 25%), or <see langword="null"/> if not provided by the source.</param>
/// <param name="OpenInterest">The number of outstanding contracts.</param>
/// <param name="Volume">The number of contracts traded on the observation date.</param>
/// <remarks>
/// Adapters emit one record per strike/expiry/type combination. OptionPricing consumes
/// these to build volatility surfaces; Trading uses them for position valuation and Greeks.
/// The <see cref="ImpliedVolatility"/> field should be in decimal form after normalization;
/// sources that publish percent-form IV (e.g., 25.0 for 25%) are converted by the normalizer.
/// </remarks>
public sealed record OptionQuote(
    DateOnly Date,
    Symbol Underlying,
    DateOnly Expiry,
    decimal Strike,
    OptionType Type,
    decimal? Bid,
    decimal? Ask,
    decimal? LastPrice,
    decimal? ImpliedVolatility,
    long OpenInterest,
    long Volume);
