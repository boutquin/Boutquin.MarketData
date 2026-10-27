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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>
/// Requests overnight benchmark fixing history (e.g., SOFR, SONIA, €STR, CORRA) over a date range.
/// </summary>
/// <param name="BenchmarkId">The benchmark identifier (see <see cref="StandardBenchmarks"/> for canonical names).</param>
/// <param name="Range">The inclusive date range for the request.</param>
/// <param name="Frequency">The observation frequency. Defaults to <see cref="DataFrequency.Daily"/>.</param>
public sealed record OvernightFixingRequest(
    BenchmarkName BenchmarkId,
    DateRange Range,
    DataFrequency Frequency = DataFrequency.Daily) : IDataRequest
{
    /// <summary>
    /// Optional regional hint — benchmarks are currency/jurisdiction-specific (SOFR/US,
    /// CORRA/CA, SONIA/GB, €STR/EU). The Exchange code lets adapter orchestration prefer a
    /// co-located source (e.g. Bank of Canada for CORRA, Fed H.15 for SOFR). When
    /// <see langword="null"/> the request is venue-neutral.
    /// </summary>
    /// <remarks>
    /// Exposed as an <c>init</c> property for 1.1.0 binary compatibility with 1.0.x consumers.
    /// </remarks>
    public ExchangeCode? Exchange { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Identifier slashes are doubled before appending an optional <c>/</c> plus
    /// <see cref="Exchange"/> suffix. Slash-free identifiers retain their legacy keys.
    /// Upgrade using a fresh snapshot directory and restart to discard old L1 entries: legacy
    /// keys can contain aliased data even at an unchanged venue-qualified key. Re-fetch the data;
    /// no automatic migration or legacy-key fallback is performed. Existing files are left untouched.
    /// </remarks>
    public string DatasetKey =>
        DividendDatasetIdentity.EncodeTickerAndExchange(BenchmarkId.Value, Exchange);
}
