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

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// The per-row contract identity <c>(Expiry, Strike, Type)</c> of a single listed option contract —
/// the discriminant half of the keyed-row fetch envelope
/// used when fetched and stored record types differ.
/// </summary>
/// <param name="Expiry">The option expiration date.</param>
/// <param name="Strike">The strike price.</param>
/// <param name="Type">Call or put.</param>
/// <remarks>
/// <para><b>Non-persisted, key-bound identity.</b> This DTO travels <b>beside</b> the fetched
/// <see cref="OptionQuote"/> body inside an <see cref="OptionChainRow"/> only between fetch and append,
/// where the per-row fan-out reads it to build the fully-populated
/// <see cref="Boutquin.MarketData.Abstractions.Keys.OptionQuoteDatasetKey"/>. It is deliberately a
/// plain record — <b>not</b> <c>[StorageSerializable]</c> and
/// <b>not</b> <c>[ColumnarStorable]</c> — so it never enters the columnar/row wire format and never
/// touches <see cref="OptionQuote"/>'s record equality. Separating fetch identity from the stored body means
/// the contract identity lives in the key, the observation body stays slim, and the two are paired only
/// at the adapter boundary.</para>
/// </remarks>
public sealed record OptionContract(DateOnly Expiry, decimal Strike, OptionType Type);
