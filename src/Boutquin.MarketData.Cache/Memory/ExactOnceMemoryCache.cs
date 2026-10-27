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
using Boutquin.MarketData.Abstractions.Contracts;

namespace Boutquin.MarketData.Cache.Memory;

/// <summary>
/// Thread-safe in-memory L1 cache with per-entry TTL and unconditional last-write-wins overwrites.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline role: fast hot-path cache in front of durable L2 snapshot storage and live adapter
/// fetches. A typical data pipeline checks this cache first so repeated requests inside the TTL
/// window return without touching disk or network.
/// </para>
/// <para>
/// Eviction: reads reclaim the expired entry they observe. Reads and writes also sweep expired
/// entries across the cache at most once per minute of activity, including reads for missing keys.
/// No background timer is allocated; an idle cache retains its entries until activity resumes.
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
    private readonly ConcurrentDictionary<string, CacheEntry> _store = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private long _nextSweepUtcTicks;

    /// <summary>Creates a cache using the system clock.</summary>
    public ExactOnceMemoryCache() : this(TimeProvider.System) { }

    /// <summary>Creates a cache using the supplied clock for expiration.</summary>
    /// <param name="timeProvider">Clock used to measure entry expiration and cleanup intervals.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider"/> is null.</exception>
    public ExactOnceMemoryCache(TimeProvider timeProvider)
        => _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <inheritdoc />
    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        _store.TryGetValue(key, out var entry);
        var now = _timeProvider.GetUtcNow();
        SweepExpiredEntries(now);
        if (entry is null)
        {
            return Task.FromResult<T?>(default);
        }

        if (entry.ExpiresAtUtc > now)
        {
            // The cache is weakly typed: a different requested type is a miss.
            return Task.FromResult(entry.Value is T typed ? typed : default);
        }

        // Reference identity ensures a concurrent replacement survives expiration of this entry.
        _store.TryRemove(new KeyValuePair<string, CacheEntry>(key, entry));
        return Task.FromResult<T?>(default);
    }

    /// <inheritdoc />
    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();
        var entry = new CacheEntry(value, now.Add(ttl));
        SweepExpiredEntries(now);
        _store[key] = entry;
        return Task.CompletedTask;
    }

    private void SweepExpiredEntries(DateTimeOffset now)
    {
        var scheduled = Volatile.Read(ref _nextSweepUtcTicks);
        if (now.UtcTicks < scheduled)
        {
            return;
        }

        var next = Math.Min(now.UtcTicks, DateTimeOffset.MaxValue.UtcTicks - TimeSpan.TicksPerMinute)
            + TimeSpan.TicksPerMinute;
        if (Interlocked.CompareExchange(ref _nextSweepUtcTicks, next, scheduled) != scheduled)
        {
            return;
        }

        foreach (var entry in _store)
        {
            if (entry.Value.ExpiresAtUtc <= now)
            {
                _store.TryRemove(entry);
            }
        }
    }

    private sealed class CacheEntry(object? value, DateTimeOffset expiresAtUtc)
    {
        public object? Value { get; } = value;
        public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
    }
}
