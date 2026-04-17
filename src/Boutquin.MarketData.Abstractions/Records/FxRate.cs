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

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// A foreign-exchange spot rate observation for a single date and currency pair.
/// </summary>
/// <param name="Date">The observation date.</param>
/// <param name="BaseCurrency">The ISO 4217 base currency.</param>
/// <param name="QuoteCurrency">The ISO 4217 quote currency.</param>
/// <param name="Rate">The exchange rate: 1 unit of <paramref name="BaseCurrency"/> equals this many units of <paramref name="QuoteCurrency"/>.</param>
public sealed record FxRate(
    DateOnly Date,
    CurrencyCode BaseCurrency,
    CurrencyCode QuoteCurrency,
    decimal Rate);
