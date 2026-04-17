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
using Boutquin.Storage.Infrastructure.KeyValueStore;

namespace Boutquin.MarketData.Cache.Memory;

/// <summary>
/// Thread-safe in-memory L1 cache with per-entry TTL, backed by
/// <see cref="ConcurrentKeyValueStore{TKey,TValue}"/> for exact-once write semantics.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline role: fast hot-path cache in front of durable L2 snapshot storage and live adapter
/// fetches. A typical data pipeline checks this cache first so repeated requests inside the TTL
/// window return without touching disk or network.
/// </para>
/// <para>
/// Eviction: expired entries are reclaimed lazily on the next read for that key rather than by
/// a background sweeper. This keeps the cache allocation-free at idle at the cost of retaining
/// memory for expired entries that are never read again. For workloads with many short-lived
/// keys, schedule a periodic external sweep.
/// </para>
/// <para>
/// Concurrency: two concurrent readers of an expired key both observe the miss, both trigger
/// the remove, and both return <c>default</c>. They will each proceed to recompute the value
/// independently — this cache does not coalesce concurrent cold misses. Wrap with
/// <see cref="Concurrent.LazyAsyncCache{TKey,TValue}"/> when thundering-herd protection is required.
/// </para>
/// <para>
/// Write semantics: <c>SetAsync</c> overwrites unconditionally, including entries still within
/// their TTL. Callers needing compare-and-swap or "set if absent" must coordinate externally
/// (for example via <see cref="Concurrent.LazyAsyncCache{TKey,TValue}"/>).
/// </para>
/// </remarks>
public sealed class ExactOnceMemoryCache : ILocalCache
{
    private readonly ConcurrentKeyValueStore<string, CacheEntry> _store = new();

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var (entry, found) = await _store.TryGetValueAsync(key, cancellationToken).ConfigureAwait(false);
        if (!found)
        {
            // Miss: nothing to remove. Avoid a redundant RemoveAsync that would also race
            // with a concurrent SetAsync installing a fresh entry.
            return default;
        }

        if (entry.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            // Type-mismatch safety: the cache is weakly typed (CacheEntry.Value is object?),
            // so a caller retrieving under a different T than was stored would otherwise hit
            // an InvalidCastException. Treat mismatch as a miss rather than crashing the
            // consumer — the caller can then refetch with the correct type.
            return entry.Value is T typed ? typed : default;
        }

        // Expired — best-effort reclaim. This is a TOCTOU race by construction: between our
        // read and the remove, a concurrent SetAsync could install a new entry under the
        // same key. That write would be lost. Accepted trade-off because the underlying
        // ConcurrentKeyValueStore does not expose a conditional "remove-if-equals" primitive.
        // Consumers requiring exact-once write semantics through this race must compose with
        // LazyAsyncCache which coalesces cold misses.
        await _store.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        return default;
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        var entry = new CacheEntry(value, DateTimeOffset.UtcNow.Add(ttl));
        await _store.SetAsync(key, entry, cancellationToken).ConfigureAwait(false);
    }

    private sealed record CacheEntry(object? Value, DateTimeOffset ExpiresAtUtc);
}
