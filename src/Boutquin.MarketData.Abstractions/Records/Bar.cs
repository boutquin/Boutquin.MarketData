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
/// An OHLCAV price bar representing a single trading period for an equity, ETF, or similar instrument.
/// </summary>
/// <param name="Date">The trading date of the bar.</param>
/// <param name="Open">Opening price.</param>
/// <param name="High">Highest price during the period.</param>
/// <param name="Low">Lowest price during the period.</param>
/// <param name="Close">Closing (unadjusted) price.</param>
/// <param name="AdjustedClose">Split- and dividend-adjusted closing price.</param>
/// <param name="Volume">Number of shares or contracts traded.</param>
/// <remarks>
/// <para><b>Partial + <see cref="IHasDate"/>:</b> declared partial so the Boutquin.Storage columnar
/// source generator can emit a companion declaration when <c>[ColumnarStorable]</c> is applied.
/// <see cref="IHasDate"/> surfaces <see cref="Date"/> as the sort-defining timestamp for the
/// time-series store.</para>
/// <para><b>Columnar default:</b> <c>[ColumnarStorable]</c> declares this type opt-in for the
/// columnar on-disk segment format. Field codecs use
/// the generator-selected defaults from CLR types (<c>DateOnly</c> → <c>DeltaOfDeltaVlq</c>,
/// <c>decimal</c> → <c>Dictionary</c>/<c>ScaledIntegerBitPacked</c> fallback,
/// <c>long</c> → <c>DeltaVlq</c>); no per-field <c>[ColumnEncoding]</c> overrides are applied.</para>
/// </remarks>
[StorageSerializable]
[ColumnarStorable]
public partial record struct Bar(
    [property: Tag(1)] DateOnly Date,
    [property: Tag(2)] decimal Open,
    [property: Tag(3)] decimal High,
    [property: Tag(4)] decimal Low,
    [property: Tag(5)] decimal Close,
    [property: Tag(6)] decimal AdjustedClose,
    [property: Tag(7)] long Volume) : IHasDate;
