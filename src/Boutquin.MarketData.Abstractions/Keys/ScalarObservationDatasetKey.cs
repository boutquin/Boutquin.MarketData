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
/// Composite partition key for a scalar economic/benchmark series:
/// <c>(Provider, SeriesId)</c>. Used by adapters like FRED (<c>"fred"</c> provider,
/// series IDs like <c>"DGS10"</c>, <c>"GDP"</c>).
/// </summary>
/// <param name="Provider">Adapter-assigned provider identifier.</param>
/// <param name="SeriesId">Provider-scoped series identifier.</param>
/// <remarks>
/// See <see cref="PriceBarDatasetKey"/> remarks for the Storage 2.0 tagged-ordered-wire design and
/// lexicographic ordering guarantee. Both fields stay <c>string</c> — provider is an open registry
/// and <c>SeriesId</c> is a source-opaque identifier with no closed vocabulary to enum-substitute.
/// </remarks>
[Key]
public readonly partial record struct ScalarObservationDatasetKey(
    [property: Tag(1)] string Provider,
    [property: Tag(2)] string SeriesId);
