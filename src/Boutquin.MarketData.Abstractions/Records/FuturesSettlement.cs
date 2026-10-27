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
using Boutquin.Storage.Domain.Attributes;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// A futures contract settlement observation, typically from an exchange end-of-day report.
/// </summary>
/// <param name="Date">The settlement date.</param>
/// <param name="ContractCode">The product code (e.g., <c>new FuturesProductCode("SR3")</c> for 3-month SOFR futures).</param>
/// <param name="ContractMonth">The delivery month (canonical <c>YYYY-MM</c>).</param>
/// <param name="SettlePrice">The daily settlement price.</param>
/// <param name="ImpliedRate">
/// The implied interest rate in decimal form (for example, <c>0.025</c> for 2.5%), derived as
/// <c>(100 - price) / 100</c> for price-quoted rate products such as <c>SR3</c> and <c>ZQ</c>, or
/// <see langword="null"/> when the product does not carry that meaning.
/// </param>
/// <remarks>
/// <para>Declared as a <c>partial record struct</c> implementing <see cref="IHasDate"/> so the
/// storage generator can add serialization members and the time-series store can order rows.</para>
/// <para><b>Tagged-wire serializable, row opt-out.</b> This record carries
/// <c>[StorageSerializable]</c>, so the Boutquin.Storage 2.0 source generator emits
/// <c>Serialize</c>/<c>Deserialize</c> and the type satisfies
/// <c>ISerializable&lt;FuturesSettlement&gt;</c> — making it eligible for the row-format
/// <c>ITimeSeriesStore</c> registration. Its value-object wrappers
/// <see cref="FuturesProductCode"/> and <see cref="ContractMonth"/> are themselves
/// <c>[StorageSerializable]</c>, so each nests as a length-prefixed child payload on the wire and
/// reconstructs through its own validating constructor; <see cref="ImpliedRate"/> is a natively
/// tagged nullable <c>decimal?</c> (absent tag ⇒ <see langword="null"/>).</para>
/// <para>It is deliberately not <c>[ColumnarStorable]</c>. The nested record-struct wrappers and
/// nullable <c>decimal?</c> have no supported columnar encoding, so the row codec is used.</para>
/// </remarks>
[StorageSerializable]
public partial record struct FuturesSettlement(
    [property: Tag(1)] DateOnly Date,
    [property: Tag(2)] FuturesProductCode ContractCode,
    [property: Tag(3)] ContractMonth ContractMonth,
    [property: Tag(4)] decimal SettlePrice,
    [property: Tag(5)] decimal? ImpliedRate) : IHasDate;
