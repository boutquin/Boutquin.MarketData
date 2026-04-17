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
namespace Boutquin.MarketData.Normalization.Filters;

/// <summary>
/// Filters collections of date-keyed records to a specified date range.
/// </summary>
/// <remarks>
/// Used by L1 caching layers for superset filtering: when the cache holds data for a broader
/// date range than requested, this filter narrows the result in memory instead of re-fetching.
/// Both boundaries are inclusive. Null boundaries are unbounded (no filtering on that end).
/// </remarks>
public static class DateRangeFilter
{
    /// <summary>
    /// Filters date-keyed pairs to those within the specified inclusive date range.
    /// </summary>
    /// <typeparam name="TValue">The value type paired with each date.</typeparam>
    /// <param name="source">The source collection of date-keyed pairs.</param>
    /// <param name="from">The inclusive start date, or <see langword="null"/> for no lower bound.</param>
    /// <param name="to">The inclusive end date, or <see langword="null"/> for no upper bound.</param>
    /// <returns>Pairs whose date falls within the specified range.</returns>
    public static IEnumerable<KeyValuePair<DateOnly, TValue>> Apply<TValue>(
        IEnumerable<KeyValuePair<DateOnly, TValue>> source,
        DateOnly? from,
        DateOnly? to)
    {
        foreach (var item in source)
        {
            if (from.HasValue && item.Key < from.Value)
            {
                continue;
            }

            if (to.HasValue && item.Key > to.Value)
            {
                continue;
            }

            yield return item;
        }
    }

    /// <summary>
    /// Filters a sequence using a date selector to extract the date from each element.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The source collection.</param>
    /// <param name="dateSelector">Function to extract the date from each element.</param>
    /// <param name="from">The inclusive start date, or <see langword="null"/> for no lower bound.</param>
    /// <param name="to">The inclusive end date, or <see langword="null"/> for no upper bound.</param>
    /// <returns>Elements whose extracted date falls within the specified range.</returns>
    public static IEnumerable<T> Apply<T>(
        IEnumerable<T> source,
        Func<T, DateOnly> dateSelector,
        DateOnly? from,
        DateOnly? to)
    {
        foreach (var item in source)
        {
            var date = dateSelector(item);

            if (from.HasValue && date < from.Value)
            {
                continue;
            }

            if (to.HasValue && date > to.Value)
            {
                continue;
            }

            yield return item;
        }
    }
}
