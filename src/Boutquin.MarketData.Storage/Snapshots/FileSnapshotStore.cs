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
using System.Security.Cryptography;
using System.Text;
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.Storage.Domain.Interfaces.ObjectStore;
using Boutquin.Storage.Infrastructure.ObjectStore;

namespace Boutquin.MarketData.Storage.Snapshots;

/// <summary>
/// Durable L2 snapshot store backed by <see cref="FileSystemObjectStore"/>. Delegates I/O to
/// <see cref="IObjectStore"/>, with bounded physical keys when constructed from a root directory.
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
/// Directory-backed instances hash each valid logical key with SHA-256 into a fixed-length
/// filename beneath <c>%snapshots-v1</c>. The percent sign cannot occur in a legacy object-store
/// key, so the new physical namespace cannot alias a legacy logical key. Reads first try the
/// new layout, then earlier encoded and literal layouts; legacy names must match ordinally
/// and descendant symbolic links are rejected. Existing payloads are neither moved nor rewritten. The configured root itself must still fit the host filesystem's path limits.
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
/// Injected stores receive logical keys unchanged and retain their own path, atomicity, and
/// stream semantics; injecting a filesystem backend does not enable the bounded layout.
/// </para>
/// </remarks>
public sealed class FileSnapshotStore : ISnapshotStore
{
    private readonly IObjectStore _store;
    private readonly IObjectStore? _legacyStore;
    private readonly string? _baseDirectory;

    /// <summary>
    /// Initializes a new instance of <see cref="FileSnapshotStore"/>
    /// backed by the file system at the specified directory.
    /// </summary>
    /// <param name="baseDirectory">Root directory for snapshot files.</param>
    public FileSnapshotStore(string baseDirectory)
    {
        _legacyStore = new FileSystemObjectStore(baseDirectory);
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _store = new FileSystemObjectStore(Path.Combine(baseDirectory, "%snapshots-v1"));
    }

    /// <summary>
    /// Initializes a new instance of <see cref="FileSnapshotStore"/>
    /// using the provided <see cref="IObjectStore"/> implementation.
    /// </summary>
    /// <param name="store">The object store to delegate to with unchanged logical keys.</param>
    public FileSnapshotStore(IObjectStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string snapshotKey, CancellationToken cancellationToken = default)
    {
        if (await _store.ExistsAsync(GetPhysicalKey(snapshotKey), cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        try
        {
            return _legacyStore is not null
                && (LegacyObjectReader.Find(_baseDirectory!, "%snapshots-v1/" + GetPhysicalKey(snapshotKey), cancellationToken) is not null
                    || await _legacyStore.ExistsAsync(snapshotKey, cancellationToken).ConfigureAwait(false)
                    || LegacyObjectReader.Find(_baseDirectory!, snapshotKey, cancellationToken) is not null);
        }
        catch (PathTooLongException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<Stream?> ReadAsync(string snapshotKey, CancellationToken cancellationToken = default)
    {
        var stream = await _store.ReadAsync(GetPhysicalKey(snapshotKey), cancellationToken).ConfigureAwait(false);
        if (stream is not null || _legacyStore is null)
        {
            return stream;
        }

        try
        {
            return LegacyObjectReader.Open(_baseDirectory!, "%snapshots-v1/" + GetPhysicalKey(snapshotKey), cancellationToken)
                ?? await _legacyStore.ReadAsync(snapshotKey, cancellationToken).ConfigureAwait(false)
                ?? LegacyObjectReader.Open(_baseDirectory!, snapshotKey, cancellationToken);
        }
        catch (PathTooLongException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task WriteAsync(string snapshotKey, Stream content, CancellationToken cancellationToken = default)
        => await _store.WriteAsync(GetPhysicalKey(snapshotKey), content, cancellationToken).ConfigureAwait(false);

    private string GetPhysicalKey(string snapshotKey)
    {
        if (_legacyStore is null)
        {
            return snapshotKey;
        }

        // Preserve the filesystem backend's logical-key grammar before mapping it to an opaque name.
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotKey);
        if (snapshotKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-' and not '/')
            || snapshotKey.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("Snapshot keys must contain valid relative object-store path segments.", nameof(snapshotKey));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotKey)));
    }
}
