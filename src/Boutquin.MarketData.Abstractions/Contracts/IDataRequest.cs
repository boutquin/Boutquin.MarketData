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

using Boutquin.MarketData.Abstractions.Requests;

namespace Boutquin.MarketData.Abstractions.Contracts;

/// <summary>
/// Base interface for all typed data requests, identifying a dataset, date range, and sampling frequency.
/// </summary>
/// <remarks>
/// Domain adapters implement concrete request types (e.g., <c>BarSeriesRequest</c>,
/// <c>CurveQuoteRequest</c>) that carry additional fields while satisfying this contract.
/// The pipeline uses <see cref="DatasetKey"/> and <see cref="Frequency"/> for cache-key
/// construction and adapter routing.
/// </remarks>
public interface IDataRequest
{
    /// <summary>
    /// Key identifying the target dataset (e.g., ticker symbol, index name, or provider-specific series ID).
    /// </summary>
    string DatasetKey { get; }

    /// <summary>
    /// Inclusive date range bounding the data to fetch.
    /// </summary>
    DateRange Range { get; }

    /// <summary>
    /// Sampling frequency of the requested data (e.g., daily, weekly).
    /// </summary>
    DataFrequency Frequency { get; }

    /// <summary>
    /// Returns a copy of this request narrowed to <paramref name="range"/>, preserving every
    /// other field (symbols, series id, venue, frequency). The time-series pipeline
    /// uses this to dispatch an adapter for a single coverage <b>gap</b> rather than the whole
    /// requested span, so only the missing sub-range is fetched.
    /// </summary>
    /// <param name="range">The gap sub-range to fetch.</param>
    /// <returns>A request identical to this one except for its <see cref="Range"/>.</returns>
    /// <remarks>
    /// The default implementation throws — a request type has to opt into the time-series
    /// gap-fill path by overriding this to return a range-narrowed copy of itself. Returning
    /// <c>this</c> unchanged would silently re-fetch the full range for every gap, defeating the
    /// gap-fill guarantee; throwing makes an un-migrated request type fail loudly under
    /// <c>UseTimeSeriesStore = true</c> instead.
    /// </remarks>
    IDataRequest WithRange(DateRange range)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support time-series gap-fill: override IDataRequest.WithRange "
            + "to return a range-narrowed copy before enabling UseTimeSeriesStore for this request type.");
}
