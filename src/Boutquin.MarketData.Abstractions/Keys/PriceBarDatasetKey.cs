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
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.Storage.Domain.Attributes;

namespace Boutquin.MarketData.Abstractions.Keys;

/// <summary>
/// Composite partition key identifying a single ticker's bar stream within the time-series
/// store: <c>(Provider, Frequency, Ticker, Exchange)</c>.
/// </summary>
/// <param name="Provider">Adapter-assigned provider identifier (e.g. <c>"tiingo"</c>, <c>"twelvedata"</c>). Stays <c>string</c> — the provider set is an open extensibility point owned by the adapter repo, and a typed wrapper is illegal in a <c>[Key]</c> record (BSSG014).</param>
/// <param name="Frequency">Bar granularity as the <see cref="DataFrequency"/> enum (the same enum <see cref="PriceHistoryRequest.Frequency"/> carries — no separate <c>BarGranularity</c> type).</param>
/// <param name="Ticker">Ticker symbol.</param>
/// <param name="Exchange">Optional listing venue MIC as <see cref="ExchangeCode"/>?; <see langword="null"/> when venue-neutral. Scopes the key per venue so two requests for the same ticker on different exchanges produce distinct stores.</param>
/// <remarks>
/// <para><b>Storage 2.0 tagged ordered wire:</b> under the Storage 2.0 source generator every
/// property carries an explicit <c>[Tag(N)]</c> and native enums are first-class in <c>[Key]</c>
/// records (BSSG014 rejects only typed-wrapper/nested annotated types, never enums). The 1.x
/// all-string workaround is retired: <see cref="Frequency"/> is <see cref="DataFrequency"/> and
/// <see cref="Exchange"/> is a nullable <see cref="ExchangeCode"/>. <see cref="Provider"/> stays
/// <c>string</c> (open registry; wrappers illegal in keys).</para>
/// <para><b>Ordering:</b> field declaration order is lexicographic: <c>Provider</c> →
/// <c>Frequency</c> → <c>Ticker</c> → <c>Exchange</c>. A range scan for one
/// (Provider, Frequency, Ticker) triple hits a contiguous key range in the LSM engine, letting
/// the time-series store answer <c>GetRangeAsync</c> with a single sequential read.</para>
/// <para><b>Generator emission:</b> <see cref="KeyAttribute"/> drives the source generator to
/// emit <c>ISerializable&lt;T&gt;</c>, <c>IComparable&lt;T&gt;</c>, and <c>CompareTo(T other)</c>
/// with lex ordering over the record parameters. <c>IEquatable&lt;T&gt;</c> is auto-synthesized
/// by the record-struct declaration. The <c>new()</c> constraint required by
/// <c>ITimeSeriesStorageEngine</c> is satisfied by the record-struct's implicit default
/// constructor.</para>
/// </remarks>
[Key]
public readonly partial record struct PriceBarDatasetKey(
    [property: Tag(1)] string Provider,
    [property: Tag(2)] DataFrequency Frequency,
    [property: Tag(3)] string Ticker,
    [property: Tag(4)] ExchangeCode? Exchange);
