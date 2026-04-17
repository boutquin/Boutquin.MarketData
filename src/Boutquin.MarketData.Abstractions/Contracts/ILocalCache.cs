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

namespace Boutquin.MarketData.Abstractions.Contracts;

/// <summary>
/// L1 in-memory cache for short-lived data, reducing redundant fetches within a pipeline execution.
/// </summary>
/// <remarks>
/// Implementations must be thread-safe. Expired entries are evicted lazily on read,
/// not proactively. The cache does not persist across process restarts; use
/// <see cref="ISnapshotStore"/> for durable storage.
/// </remarks>
public interface ILocalCache
{
    /// <summary>
    /// Retrieves a cached value by key, or <see langword="null"/> if the key is absent or expired.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The cached value, or <see langword="null"/> if not found.</returns>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a value in the cache with the specified time-to-live.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="ttl">The duration before the entry expires.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default);
}
