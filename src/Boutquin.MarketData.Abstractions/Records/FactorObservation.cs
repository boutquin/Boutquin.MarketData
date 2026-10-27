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
/// A single academic factor's return for one date and one factor name (e.g., Fama-French
/// "Mkt-RF", "SMB", "HML", "RMW", "CMA", momentum), in decimal form.
/// </summary>
/// <param name="Date">The observation date.</param>
/// <param name="FactorName">The factor identifier (e.g., "Mkt-RF", "SMB", "HML", "RMW", "CMA", "RF", "Mom", "WML").</param>
/// <param name="Value">The factor's return for <paramref name="Date"/>, in decimal form (e.g., 0.0039 for 0.39%).</param>
/// <remarks>
/// <para><b>One record per factor:</b> a multi-factor observation date (a Fama-French CSV row
/// listing Mkt-RF, SMB, HML, …) produces one <see cref="FactorObservation"/> per factor, all
/// sharing the same <see cref="Date"/>. Consumers that need the row-wise view of every factor for
/// a date group by <see cref="Date"/>.</para>
/// <para><b>Partial + <see cref="IHasDate"/>:</b> declared partial so the Boutquin.Storage columnar
/// source generator can emit a companion declaration when <c>[ColumnarStorable]</c> is applied.
/// <see cref="IHasDate"/> surfaces <see cref="Date"/> as the sort-defining timestamp for the
/// time-series store.</para>
/// <para><b>Columnar-native:</b> <c>[ColumnarStorable]</c> declares this type opt-in for the
/// columnar on-disk segment format. The <see cref="FactorName"/> string column dictionary-encodes,
/// so runs of the same factor compress; codecs use the generator-selected defaults from CLR types
/// (<c>DateOnly</c> → <c>DeltaOfDeltaVlq</c>, <c>string</c> → <c>Dictionary</c>,
/// <c>decimal</c> → <c>Dictionary</c>).</para>
/// </remarks>
[StorageSerializable]
[ColumnarStorable]
public partial record struct FactorObservation(
    [property: Tag(1)] DateOnly Date,
    [property: Tag(2)] string FactorName,
    [property: Tag(3)] decimal Value) : IHasDate;
