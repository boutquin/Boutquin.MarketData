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

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>
/// Specifies the sampling frequency for market data requests, controlling the
/// temporal granularity of returned data points.
/// </summary>
/// <remarks>
/// The frequency is used by the pipeline for cache-key construction, adapter routing,
/// and coverage calculation. Not all adapters support all frequencies; unsupported
/// values cause the adapter's <c>CanHandle</c> method to return
/// <see langword="false"/>.
/// </remarks>
public enum DataFrequency
{
    /// <summary>
    /// Tick-by-tick data representing every individual trade or quote update.
    /// Produces the highest data volume and requires specialized storage.
    /// </summary>
    Tick,

    /// <summary>
    /// Intraday bars aggregated at sub-daily intervals (e.g., 1-minute, 5-minute, or hourly).
    /// The specific interval is determined by the adapter or request parameters.
    /// </summary>
    Intraday,

    /// <summary>
    /// End-of-day bars representing a single data point per trading day.
    /// This is the most widely available frequency across free and public data sources.
    /// </summary>
    Daily,

    /// <summary>
    /// Weekly bars aggregating trading activity across a full calendar week.
    /// Typically aligned to Friday close or the last trading day of the week.
    /// </summary>
    Weekly,

    /// <summary>
    /// Monthly bars aggregating trading activity across a full calendar month.
    /// Typically aligned to the last trading day of the month.
    /// </summary>
    Monthly
}
