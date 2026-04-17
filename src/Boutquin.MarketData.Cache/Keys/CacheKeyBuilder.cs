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
using System.Globalization;

namespace Boutquin.MarketData.Cache.Keys;

/// <summary>
/// Utility for building deterministic cache keys from collections and optional date ranges.
/// </summary>
/// <remarks>
/// Keys are built by sorting elements lexicographically and joining with a pipe separator.
/// This ensures that the same logical request (regardless of input ordering) always produces
/// the same cache key, preventing duplicate entries for reordered parameters.
/// </remarks>
public static class CacheKeyBuilder
{
    /// <summary>
    /// Builds a cache key from a prefix and a sorted collection of string segments.
    /// </summary>
    /// <param name="prefix">A namespace prefix for the key (e.g., "market", "fx", "fred").</param>
    /// <param name="segments">The segments to sort and join. Order-independent.</param>
    /// <returns>A deterministic cache key string.</returns>
    public static string FromSegments(string prefix, IEnumerable<string> segments) =>
        $"{prefix}/{string.Join("/", segments.OrderBy(s => s, StringComparer.Ordinal))}";

    /// <summary>
    /// Builds a cache key for a date-range query, using wildcards for unbounded endpoints.
    /// </summary>
    /// <param name="prefix">A namespace prefix for the key.</param>
    /// <param name="identifier">The primary identifier (e.g., series ID, dataset key).</param>
    /// <param name="from">The start date, or <see langword="null"/> for unbounded start.</param>
    /// <param name="to">The end date, or <see langword="null"/> for unbounded end.</param>
    /// <returns>A deterministic cache key string with date range encoding.</returns>
    /// <remarks>
    /// Uses <c>/</c> as separator (compatible with <c>FileSystemObjectStore</c> key validation).
    /// Date format is ISO 8601 (e.g., <c>2026-04-12</c>). Unbounded endpoints use <c>_</c>.
    /// </remarks>
    public static string FromDateRange(string prefix, string identifier, DateOnly? from, DateOnly? to) =>
        $"{prefix}/{identifier}/{(from.HasValue ? from.Value.ToString("O", CultureInfo.InvariantCulture) : "_")}/{(to.HasValue ? to.Value.ToString("O", CultureInfo.InvariantCulture) : "_")}";
}
