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
/// The keyed-row fetch envelope for an option chain: one fetched row pairing a contract's
/// identity (<see cref="OptionContract"/>) with its per-date quote body (<see cref="OptionQuote"/>).
/// An option-chain adapter emits <see cref="Boutquin.MarketData.Abstractions.Contracts.IDataSourceAdapter{TRequest, TRecord}">
/// <c>IDataSourceAdapter&lt;OptionChainRequest, OptionChainRow&gt;</c></see>; the per-row fan-out then
/// builds the fully-populated <see cref="Boutquin.MarketData.Abstractions.Keys.OptionQuoteDatasetKey"/>
/// from <see cref="Contract"/> and stores the slim <see cref="Quote"/> under it
/// with a distinct identity and stored body.
/// </summary>
/// <param name="Contract">The per-row contract identity <c>(Expiry, Strike, Type)</c>.</param>
/// <param name="Quote">The per-date observation body written to the store.</param>
/// <remarks>
/// <para><b>Fetched, never stored.</b> This is the <c>TFetched</c> type of the
/// <see cref="Boutquin.MarketData.Abstractions.Contracts.IDataSourceAdapter{TRequest, TRecord}"/> for an option chain — distinct from the
/// <c>TStored</c> <see cref="OptionQuote"/> the fan-out actually appends. It is deliberately a plain
/// record — <b>not</b> <c>[StorageSerializable]</c> — so no wire format or record equality change
/// reaches <see cref="OptionQuote"/>: the identity is carried <em>beside</em> the body, never
/// <em>on</em> it. The contract identity is hoisted into the dataset key by the fan-out's
/// <c>Project</c>; the body is written slim, preserving the invariant that
/// <see cref="OptionQuote"/> carries only the per-date observation.</para>
/// </remarks>
public sealed record OptionChainRow(OptionContract Contract, OptionQuote Quote);
