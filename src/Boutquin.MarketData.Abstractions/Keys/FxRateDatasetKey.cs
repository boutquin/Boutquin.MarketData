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

using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.Storage.Domain.Attributes;

namespace Boutquin.MarketData.Abstractions.Keys;

/// <summary>
/// Composite partition key for an FX rate series:
/// <c>(Provider, BaseCurrency, QuoteCurrency, Frequency)</c>. Ordering groups all pairs from
/// one provider together, then by base currency, so a scan for "all EUR-base pairs from
/// Frankfurter" hits a contiguous range.
/// </summary>
/// <param name="Provider">Adapter-assigned provider identifier. Stays <c>string</c> (open registry; wrappers illegal in keys, BSSG014).</param>
/// <param name="BaseCurrency">ISO 4217 base currency as the <see cref="Currency"/> enum.</param>
/// <param name="QuoteCurrency">ISO 4217 quote currency as the <see cref="Currency"/> enum.</param>
/// <param name="Frequency">Observation frequency as the <see cref="DataFrequency"/> enum.</param>
/// <remarks>
/// See <see cref="PriceBarDatasetKey"/> remarks for the Storage 2.0 tagged-ordered-wire design
/// (native enums in keys, <c>string</c> provider) and the lexicographic ordering guarantee.
/// </remarks>
[Key]
public readonly partial record struct FxRateDatasetKey(
    [property: Tag(1)] string Provider,
    [property: Tag(2)] Currency BaseCurrency,
    [property: Tag(3)] Currency QuoteCurrency,
    [property: Tag(4)] DataFrequency Frequency);
