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
/// Requests academic factor return series (e.g., Fama-French 3-factor, 5-factor, momentum).
/// </summary>
/// <param name="DatasetName">The factor dataset name (e.g., <c>new FactorDatasetId("F-F_Research_Data_Factors_daily")</c>).</param>
/// <param name="Range">The inclusive date range for the request.</param>
/// <param name="Frequency">The observation frequency. Defaults to <see cref="DataFrequency.Daily"/>.</param>
public sealed record FactorSeriesRequest(
    FactorDatasetId DatasetName,
    DateRange Range,
    DataFrequency Frequency = DataFrequency.Daily) : IDataRequest
{
    /// <summary>
    /// Optional regional hint — factor datasets are market-scoped (Fama-French US series,
    /// Asness-Moskowitz global series, etc.). Multi-region portfolios can carry the Exchange
    /// code to disambiguate which factor set applies to which sleeve. When
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
        DividendDatasetIdentity.EncodeTickerAndExchange(DatasetName.Value, Exchange);

    /// <inheritdoc />
    /// <remarks>
    /// Preserves the <see cref="DatasetName"/>, the optional <see cref="Exchange"/> venue hint, and
    /// the <see cref="DataFrequency"/>; only <see cref="IDataRequest.Range"/> is narrowed to the gap.
    /// Used by the time-series gap-fill to fetch just a missing sub-range.
    /// </remarks>
    public IDataRequest WithRange(DateRange range) =>
        new FactorSeriesRequest(DatasetName, range, Frequency) { Exchange = Exchange };
}
