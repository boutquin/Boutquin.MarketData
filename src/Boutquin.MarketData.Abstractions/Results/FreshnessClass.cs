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

namespace Boutquin.MarketData.Abstractions.Results;

/// <summary>
/// Classifies how current the underlying data is, independent of how it was retrieved.
/// </summary>
/// <remarks>
/// <see cref="DataProvenance.RetrievalMode"/> describes <em>how</em> data was obtained
/// (API call vs. cache hit). <see cref="FreshnessClass"/> describes <em>how current</em>
/// the data is at source. A cache hit can still be <see cref="Live"/> if the data was
/// fetched moments ago; an API call can return <see cref="Stale"/> data if the source
/// has not updated. Adapters set freshness based on source metadata (HTTP headers,
/// timestamps in the response, known publication schedules).
/// </remarks>
public enum FreshnessClass
{
    /// <summary>
    /// Real-time or near-real-time data, typically less than a few minutes old.
    /// Examples: streaming quotes, live central bank fixings during publication window.
    /// </summary>
    Live,

    /// <summary>
    /// Data published with a known delay (e.g., 15-minute delayed exchange quotes,
    /// T+1 settlement data). Still suitable for most analytical and backtesting uses.
    /// </summary>
    Delayed,

    /// <summary>
    /// End-of-day snapshot, published after market close. The most common freshness
    /// class for free and public data sources.
    /// </summary>
    EndOfDay,

    /// <summary>
    /// Data that has not been updated within its expected publication schedule.
    /// May indicate a source outage, holiday gap, or stale cache. Consumers should
    /// check <see cref="DataProvenance.RetrievedAtUtc"/> to assess the degree of staleness.
    /// </summary>
    Stale,

    /// <summary>
    /// Freshness cannot be determined. Used when the source provides no publication
    /// timestamp or schedule metadata.
    /// </summary>
    Unknown
}
