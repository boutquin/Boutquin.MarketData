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

namespace Boutquin.MarketData.Abstractions.Keys;

/// <summary>
/// Composite partition key for a futures-settlement series:
/// <c>(Provider, ContractCode, ContractMonth)</c>. Every settlement from one contract-month
/// lives under a contiguous prefix so the time-series store can range-scan all settlement
/// observations for <c>SR3 2026-03</c> as a single read.
/// </summary>
/// <param name="Provider">Adapter-assigned provider identifier.</param>
/// <param name="ContractCode">Exchange-assigned product code (e.g. <c>"SR3"</c>, <c>"ES"</c>).</param>
/// <param name="ContractMonth">Canonical <c>YYYY-MM</c> delivery month.</param>
/// <remarks>
/// See <see cref="PriceBarDatasetKey"/> remarks for the Storage 2.0 tagged-ordered-wire design and
/// lexicographic ordering guarantee. All three fields stay <c>string</c> — provider is an open
/// registry, and the product code / contract month are source-opaque identifiers with no closed
/// reference-data vocabulary to enum-substitute.
/// </remarks>
[Key]
public readonly partial record struct FuturesSettlementDatasetKey(
    [property: Tag(1)] string Provider,
    [property: Tag(2)] string ContractCode,
    [property: Tag(3)] string ContractMonth);
