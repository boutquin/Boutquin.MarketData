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
using Boutquin.Storage.Domain.Interfaces.ObjectStore;
using Boutquin.Storage.Infrastructure.ObjectStore;

namespace Boutquin.MarketData.Storage.InMemory;

/// <summary>
/// Volatile in-memory snapshot store for unit tests and single-process batch runs whose
/// snapshots are intentionally discarded at exit. Delegates to <see cref="InMemoryObjectStore"/>
/// for concurrent-safe storage.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline role: stand-in for <see cref="Snapshots.FileSnapshotStore"/> when durability is not
/// required — for example when verifying orchestration logic in tests, or when running an
/// ephemeral process whose snapshot output is consumed in-memory rather than persisted.
/// </para>
/// <para>
/// Durability: none. Snapshots do not survive process exit. If a workload must resume after
/// restart, use <see cref="Snapshots.FileSnapshotStore"/> instead.
/// </para>
/// <para>
/// Concurrency: thread-safe via the underlying
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>. Reads return
/// independent stream copies so readers and writers do not contend.
/// </para>
/// <para>
/// Key semantics: case-sensitive, matching the file-backed implementation so tests expose the
/// same collision surface as production.
/// </para>
/// </remarks>
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly IObjectStore _store;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemorySnapshotStore"/>
    /// using the default <see cref="InMemoryObjectStore"/>.
    /// </summary>
    public InMemorySnapshotStore()
        => _store = new InMemoryObjectStore();

    /// <summary>
    /// Initializes a new instance of <see cref="InMemorySnapshotStore"/>
    /// using the provided <see cref="IObjectStore"/> implementation.
    /// </summary>
    /// <param name="store">The object store to delegate to.</param>
    public InMemorySnapshotStore(IObjectStore store)
        => _store = store;

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string snapshotKey, CancellationToken cancellationToken = default)
        => await _store.ExistsAsync(snapshotKey, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Stream?> ReadAsync(string snapshotKey, CancellationToken cancellationToken = default)
        => await _store.ReadAsync(snapshotKey, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task WriteAsync(string snapshotKey, Stream content, CancellationToken cancellationToken = default)
        => await _store.WriteAsync(snapshotKey, content, cancellationToken).ConfigureAwait(false);
}
