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

namespace Boutquin.MarketData.Cache.WriteThrough;

/// <summary>
/// L2 write-through cache that persists data to durable storage on miss and reads from storage on hit.
/// Combines <see cref="ISnapshotStore"/> for persistence with <see cref="ISnapshotSerializer{T}"/>
/// for domain-specific serialization.
/// </summary>
/// <typeparam name="TKey">The cache key type (e.g., ticker symbol, series ID).</typeparam>
/// <typeparam name="TData">The cached data type.</typeparam>
/// <remarks>
/// <para>
/// This class encapsulates the write-through pattern found in Trading's caching decorators:
/// check if cache file exists → if yes, deserialize and return → if no, invoke factory,
/// serialize and write, then return the data.
/// </para>
/// <para>
/// The cache is single-key: one call to <see cref="GetOrFetchAsync"/> operates on exactly one
/// key. Per-symbol partitioning (e.g., checking which symbols in a batch are cached vs uncached)
/// is the responsibility of the calling decorator, not this class.
/// </para>
/// <para>
/// Atomicity depends on the underlying <see cref="ISnapshotStore"/> implementation.
/// The file-system store (FileSnapshotStore) provides atomic writes via temp-file-plus-rename.
/// </para>
/// </remarks>
public sealed class WriteThroughCache<TKey, TData>
    where TKey : notnull
{
    private readonly ISnapshotStore _store;
    private readonly ISnapshotSerializer<TData> _serializer;
    private readonly Func<TKey, string> _keyMapper;

    /// <summary>
    /// Initializes a new write-through cache.
    /// </summary>
    /// <param name="store">The durable storage backend for persisted snapshots.</param>
    /// <param name="serializer">The domain-specific serializer for <typeparamref name="TData"/>.</param>
    /// <param name="keyMapper">Maps a domain key to a snapshot store key string.</param>
    public WriteThroughCache(
        ISnapshotStore store,
        ISnapshotSerializer<TData> serializer,
        Func<TKey, string> keyMapper)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _keyMapper = keyMapper ?? throw new ArgumentNullException(nameof(keyMapper));
    }

    /// <summary>
    /// Checks whether a cached entry exists for the specified key.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns><see langword="true"/> if a cached entry exists; otherwise <see langword="false"/>.</returns>
    public Task<bool> ExistsAsync(TKey key, CancellationToken cancellationToken = default) =>
        _store.ExistsAsync(_keyMapper(key), cancellationToken);

    /// <summary>
    /// Reads and deserializes cached data for the specified key, or returns <see langword="null"/> if not cached.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The deserialized data, or <see langword="null"/> if no cached entry exists.</returns>
    public async Task<TData?> TryReadAsync(TKey key, CancellationToken cancellationToken = default)
    {
        var snapshotKey = _keyMapper(key);
        var stream = await _store.ReadAsync(snapshotKey, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            return default;
        }

        // await using is required for streams returned across an await boundary — the sync
        // Dispose can block on pending I/O for file-backed or network-backed implementations.
        await using (stream.ConfigureAwait(false))
        {
            return await _serializer.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Serializes and writes data to the cache for the specified key, overwriting any existing entry.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="data">The data to cache.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public async Task WriteAsync(TKey key, TData data, CancellationToken cancellationToken = default)
    {
        var snapshotKey = _keyMapper(key);
        var stream = new MemoryStream();
        await using (stream.ConfigureAwait(false))
        {
            await _serializer.SerializeAsync(stream, data, cancellationToken).ConfigureAwait(false);
            stream.Position = 0;
            await _store.WriteAsync(snapshotKey, stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns cached data if available; otherwise invokes the factory, caches the result, and returns it.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="factory">Async factory invoked on cache miss to produce the data.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The cached or newly fetched data.</returns>
    /// <remarks>
    /// If the factory throws, no data is written to the cache. The exception propagates to the caller.
    /// </remarks>
    public async Task<TData> GetOrFetchAsync(
        TKey key,
        Func<TKey, CancellationToken, Task<TData>> factory,
        CancellationToken cancellationToken = default)
    {
        var snapshotKey = _keyMapper(key);

        // Try cache hit. await using for file/network-backed streams (see TryReadAsync note).
        var existing = await _store.ReadAsync(snapshotKey, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await using (existing.ConfigureAwait(false))
            {
                return await _serializer.DeserializeAsync(existing, cancellationToken).ConfigureAwait(false);
            }
        }

        // Cache miss — fetch, write, return. No concurrent-miss coalescing: N concurrent
        // callers with the same cold key will each run factory and each write. Compose with
        // LazyAsyncCache when thundering-herd protection is required.
        var data = await factory(key, cancellationToken).ConfigureAwait(false);
        await WriteAsync(key, data, cancellationToken).ConfigureAwait(false);
        return data;
    }
}
