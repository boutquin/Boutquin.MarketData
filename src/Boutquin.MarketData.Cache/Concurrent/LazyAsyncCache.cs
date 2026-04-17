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
using System.Collections.Concurrent;

namespace Boutquin.MarketData.Cache.Concurrent;

/// <summary>
/// Thread-safe async cache guaranteeing exactly-once computation per key.
/// Concurrent callers requesting the same key share a single factory invocation
/// via <see cref="Lazy{T}"/> wrapping a <see cref="Task{TResult}"/>.
/// </summary>
/// <typeparam name="TKey">The cache key type.</typeparam>
/// <typeparam name="TValue">The cached value type.</typeparam>
/// <remarks>
/// <para>
/// Pipeline role: thundering-herd protection in front of expensive fetches. Without coalescing,
/// N concurrent consumers requesting the same cold key each trigger an independent adapter call
/// — for a 5000-symbol batch hitting a rate-limited API, that is both wasteful and likely to
/// breach upstream quotas. This cache collapses those N calls into one.
/// </para>
/// <para>
/// Unlike <see cref="Memory.ExactOnceMemoryCache"/> which provides a simple get/set API with TTL,
/// this cache coalesces concurrent computations: if 10 threads request the same key simultaneously,
/// only one factory call executes and all 10 await the same <see cref="Task{TResult}"/>.
/// </para>
/// <para>
/// Faulted entries are evicted automatically. If the factory throws, the <see cref="Lazy{T}"/>
/// instance is removed from the dictionary so the next caller retries with a fresh factory call.
/// This prevents permanently caching exceptions at the cost of offering no circuit-breaker
/// behavior — repeated failures cause repeated factory invocations.
/// </para>
/// <para>
/// There is no TTL or expiration — entries live until explicitly removed via <see cref="TryRemove"/>
/// or <see cref="Clear"/>. For time-bounded caching, compose this cache in front of
/// <see cref="Memory.ExactOnceMemoryCache"/>: the memory cache handles TTL and eviction, this cache
/// handles concurrent-miss coalescing.
/// </para>
/// </remarks>
public sealed class LazyAsyncCache<TKey, TValue> : IDisposable
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, Lazy<Task<TValue>>> _cache;

    /// <summary>
    /// Initializes a new instance with the default key comparer.
    /// </summary>
    public LazyAsyncCache()
        : this(EqualityComparer<TKey>.Default)
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified key comparer.
    /// </summary>
    /// <param name="comparer">The equality comparer for cache keys.</param>
    public LazyAsyncCache(IEqualityComparer<TKey> comparer)
    {
        _cache = new ConcurrentDictionary<TKey, Lazy<Task<TValue>>>(comparer);
    }

    /// <summary>
    /// Number of entries currently in the cache (including in-flight computations).
    /// </summary>
    public int Count => _cache.Count;

    /// <summary>
    /// Retrieves the value for the specified key, computing it exactly once via the factory
    /// if not already cached. Concurrent callers with the same key share the same computation.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">
    /// The async factory invoked on cache miss. Receives the key and a cancellation token.
    /// The token passed to the factory is <see cref="CancellationToken.None"/> to prevent
    /// a single caller's cancellation from faulting the shared computation.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the caller's await (not the factory).</param>
    /// <returns>The cached or newly computed value.</returns>
    /// <remarks>
    /// The factory receives <see cref="CancellationToken.None"/> intentionally. Since multiple
    /// callers share one computation, cancelling via one caller's token would fault the result
    /// for all waiters. Callers can still cancel their own await via <paramref name="cancellationToken"/>.
    /// </remarks>
    public async Task<TValue> GetOrAddAsync(
        TKey key,
        Func<TKey, CancellationToken, Task<TValue>> factory,
        CancellationToken cancellationToken = default)
    {
        var lazy = _cache.GetOrAdd(key, k => new Lazy<Task<TValue>>(
            () => factory(k, CancellationToken.None)));

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled their await — don't evict the entry (the computation may still succeed for others).
            throw;
        }
        catch
        {
            // Factory faulted — evict this specific Lazy instance so the next caller retries.
            _cache.TryRemove(KeyValuePair.Create(key, lazy));
            throw;
        }
    }

    /// <summary>
    /// Removes the entry for the specified key if present.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns><see langword="true"/> if the entry was removed; otherwise <see langword="false"/>.</returns>
    public bool TryRemove(TKey key) =>
        _cache.TryRemove(key, out _);

    /// <summary>
    /// Removes all entries from the cache.
    /// </summary>
    public void Clear() => _cache.Clear();

    /// <summary>
    /// Clears the cache. Equivalent to <see cref="Clear"/>.
    /// </summary>
    public void Dispose() => Clear();
}
