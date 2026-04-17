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

namespace Boutquin.MarketData.Storage.Snapshots;

/// <summary>
/// Durable L2 snapshot store backed by <see cref="FileSystemObjectStore"/>. Delegates I/O to
/// <see cref="IObjectStore"/>, which performs atomic writes (temp file plus rename) and maps
/// hierarchical keys containing forward slashes onto nested directories.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline role: warm tier between the in-memory L1 cache and live adapter fetches. Survives
/// process restart, so a pipeline that crashes mid-run resumes from the last persisted snapshot
/// rather than re-paying the full fetch cost.
/// </para>
/// <para>
/// Atomicity: writes go to a temporary sibling file which is then renamed into place. On POSIX
/// and NTFS, rename is atomic within a filesystem — readers observe either the previous snapshot
/// or the new one, never a partially written file. This matters because a crashed writer would
/// otherwise leave a truncated JSON/binary blob that breaks every subsequent read.
/// </para>
/// <para>
/// Read semantics: <see cref="IObjectStore.ReadAsync"/> returns an independent
/// <see cref="MemoryStream"/> copy so the caller can dispose or seek without holding a file lock
/// that would block concurrent writers.
/// </para>
/// <para>
/// Choosing between implementations: use this store for any durable workload. Use
/// <see cref="InMemory.InMemorySnapshotStore"/> only for unit tests and single-process batch
/// runs where snapshots are intentionally discarded at process exit.
/// </para>
/// <para>
/// Testability: the <see cref="IObjectStore"/> constructor overload accepts
/// <see cref="InMemoryObjectStore"/> so tests can exercise the snapshot API surface without
/// touching the file system, and integration tests can inject a mock that simulates I/O faults.
/// </para>
/// </remarks>
public sealed class FileSnapshotStore : ISnapshotStore
{
    private readonly IObjectStore _store;

    /// <summary>
    /// Initializes a new instance of <see cref="FileSnapshotStore"/>
    /// backed by the file system at the specified directory.
    /// </summary>
    /// <param name="baseDirectory">Root directory for snapshot files.</param>
    public FileSnapshotStore(string baseDirectory)
        => _store = new FileSystemObjectStore(baseDirectory);

    /// <summary>
    /// Initializes a new instance of <see cref="FileSnapshotStore"/>
    /// using the provided <see cref="IObjectStore"/> implementation.
    /// </summary>
    /// <param name="store">The object store to delegate to.</param>
    public FileSnapshotStore(IObjectStore store)
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
