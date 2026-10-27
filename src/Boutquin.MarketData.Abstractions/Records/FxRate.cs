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
/// A foreign-exchange spot rate observation for a single date. The currency pair that
/// identifies the series is carried by <c>FxRateDatasetKey(Provider, BaseCurrency,
/// QuoteCurrency, Frequency)</c>, not by the observation row.
/// </summary>
/// <param name="Date">The observation date.</param>
/// <param name="Rate">The exchange rate for the series' pair: 1 unit of the key's base currency equals this many units of the key's quote currency.</param>
/// <remarks>
/// Stores <c>(Date, Rate)</c>; currency identity lives in
/// <c>FxRateDatasetKey</c> as two <c>Currency</c> enum columns, removing the
/// identity-in-payload denormalization. The record is now flat columnar-native (matching
/// <see cref="ScalarObservation"/>) — both <c>[StorageSerializable]</c> (row tagged wire) and
/// <c>[ColumnarStorable]</c> (column plan) apply with no encoding hazard.
/// </remarks>
[StorageSerializable]
[ColumnarStorable]
public partial record struct FxRate(
    [property: Tag(1)] DateOnly Date,
    [property: Tag(2)] decimal Rate) : IHasDate;
