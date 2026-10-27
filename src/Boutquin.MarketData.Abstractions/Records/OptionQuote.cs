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

using Boutquin.Storage.Domain.Attributes;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// A single option contract quote observation from an exchange or data provider.
/// </summary>
/// <param name="Date">The observation date.</param>
/// <param name="Bid">The best bid price, or <see langword="null"/> if unavailable.</param>
/// <param name="Ask">The best ask price, or <see langword="null"/> if unavailable.</param>
/// <param name="LastPrice">The last traded price, or <see langword="null"/> if no trades.</param>
/// <param name="ImpliedVolatility">The implied volatility in decimal form (e.g., 0.25 for 25%), or <see langword="null"/> if not provided by the source.</param>
/// <param name="OpenInterest">The number of outstanding contracts.</param>
/// <param name="Volume">The number of contracts traded on the observation date.</param>
/// <remarks>
/// <para><b>Contract identity lives in the key.</b> Each option contract
/// <c>(Underlying, Expiry, Strike, Type)</c> is its own daily-quote time series, so that identity
/// is hoisted into <see cref="Boutquin.MarketData.Abstractions.Keys.OptionQuoteDatasetKey"/> and the
/// record body carries only the per-date observation. This is required for correctness: the
/// time-series LSM key is <c>(DatasetKey, Date)</c>, so without the contract identity in the key all
/// strikes/expiries for one underlying on one date would collide under last-writer-wins. Reshaping
/// out the identity fields is an intentional pre-1.0 breaking change (operator decision A).</para>
/// <para><b>Partial + <see cref="IHasDate"/>:</b> declared <c>partial record struct</c> per the M-CR
/// record convention; <see cref="IHasDate"/> surfaces <see cref="Date"/> as the sort-defining
/// timestamp for the time-series store.</para>
/// <para><b>Tagged-wire serializable, row opt-out.</b> This record carries
/// <c>[StorageSerializable]</c>, so the Boutquin.Storage 2.0 source generator emits
/// <c>Serialize</c>/<c>Deserialize</c> and the type satisfies <c>ISerializable&lt;OptionQuote&gt;</c>.
/// It is deliberately NOT <c>[ColumnarStorable]</c> — it is the row opt-out for columnar-incompatible field types, because its
/// four nullable <c>decimal?</c> fields are BSSG020 columnar hazards and nullability is semantically
/// essential (sparse quotes: an absent tag decodes back to <see langword="null"/>, not <c>0m</c>).</para>
/// <para>The <see cref="ImpliedVolatility"/> field should be in decimal form after normalization;
/// sources that publish percent-form IV (e.g., 25.0 for 25%) are converted by the normalizer.</para>
/// </remarks>
[StorageSerializable]
public partial record struct OptionQuote(
    [property: Tag(1)] DateOnly Date,
    [property: Tag(2)] decimal? Bid,
    [property: Tag(3)] decimal? Ask,
    [property: Tag(4)] decimal? LastPrice,
    [property: Tag(5)] decimal? ImpliedVolatility,
    [property: Tag(6)] long OpenInterest,
    [property: Tag(7)] long Volume) : IHasDate;
