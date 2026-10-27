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
/// A single node on a yield curve for a given as-of date: a tenor label paired with an
/// annualized rate in decimal form.
/// </summary>
/// <param name="Date">The curve's as-of date — the observation date the node belongs to. A full
/// curve snapshot is the set of <see cref="YieldCurveQuote"/> records sharing this date; consumers
/// group by <see cref="Date"/> to reconstruct one curve.</param>
/// <param name="Tenor">The maturity label (e.g., "3M", "1Y", "10Y", "30Y").</param>
/// <param name="Rate">The annualized rate in decimal form (e.g., 0.045 for 4.5%).</param>
/// <remarks>
/// <para><b>Partial + <see cref="IHasDate"/>:</b> declared partial so the Boutquin.Storage columnar
/// source generator can emit a companion declaration when <c>[ColumnarStorable]</c> is applied.
/// <see cref="IHasDate"/> surfaces <see cref="Date"/> as the sort-defining timestamp for the
/// time-series store.</para>
/// <para><b>Columnar default:</b> <c>[ColumnarStorable]</c> declares this type opt-in for the
/// columnar on-disk segment format. Field codecs use the generator-selected defaults from CLR
/// types (<c>DateOnly</c> → <c>DeltaOfDeltaVlq</c>, <c>string</c> → <c>Dictionary</c>,
/// <c>decimal</c> → <c>Dictionary</c>); no per-field <c>[ColumnEncoding]</c> overrides are applied.</para>
/// </remarks>
[StorageSerializable]
[ColumnarStorable]
public partial record struct YieldCurveQuote(
    [property: Tag(1)] DateOnly Date,
    [property: Tag(2)] string Tenor,
    [property: Tag(3)] decimal Rate) : IHasDate;
