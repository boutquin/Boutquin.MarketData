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
using System.Text;

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.Storage.Domain.Interfaces.ObjectStore;
using Boutquin.Storage.Infrastructure.ObjectStore;

namespace Boutquin.MarketData.Storage.RawDocuments;

/// <summary>
/// Persists raw API response payloads via <see cref="IObjectStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// Keys are encoded as fixed-width hexadecimal UTF-16 code units, in bounded path components,
/// with a <c>.raw-v2</c> extension. Case and punctuation remain distinct on case-insensitive
/// filesystems. Traversal-shaped keys are rejected before encoding.
/// Existing literal <c>.raw-v2</c> files remain readable without rewriting. Invalid UTF-16
/// input and invalid persisted UTF-8 throw encoding exceptions instead of replacing characters.
/// Legacy <c>.json</c> files are not read: their lossy names cannot prove the original key.
/// Re-ingest legacy raw documents from their source into this versioned layout.
/// </para>
/// <para>
/// The underlying <see cref="FileSystemObjectStore"/> provides atomic writes
/// and hierarchical key support. The <see cref="IObjectStore"/> constructor overload
/// allows injecting <see cref="InMemoryObjectStore"/> for testing.
/// </para>
/// </remarks>
public sealed class FileRawDocumentStore : IRawDocumentStore
{
    private static readonly UTF8Encoding s_strictUtf8 = new(false, true);
    private readonly IObjectStore _store;
    private readonly string? _rootDirectory;

    /// <summary>
    /// Initializes a new instance of <see cref="FileRawDocumentStore"/>
    /// backed by the file system at the specified directory.
    /// </summary>
    /// <param name="rootDirectory">
    /// Root directory for raw document storage.
    /// </param>
    public FileRawDocumentStore(string rootDirectory)
    {
        _store = new FileSystemObjectStore(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>
    /// Initializes a new instance of <see cref="FileRawDocumentStore"/>
    /// using the provided <see cref="IObjectStore"/> implementation.
    /// </summary>
    /// <param name="store">The object store to delegate to.</param>
    public FileRawDocumentStore(IObjectStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task SaveAsync(string key, string content, CancellationToken cancellationToken = default)
    {
        var storageKey = SanitizeKey(key) + ".raw-v2";
        var stream = new MemoryStream(s_strictUtf8.GetBytes(content));
        await using (stream.ConfigureAwait(false))
        {
            await _store.WriteAsync(storageKey, stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        var storageKey = SanitizeKey(key) + ".raw-v2";
        var stream = await _store.ReadAsync(storageKey, cancellationToken).ConfigureAwait(false);
        if (stream is null && _rootDirectory is not null)
        {
            stream = LegacyObjectReader.Open(_rootDirectory, storageKey, cancellationToken);
        }

        if (stream is null)
        {
            return null;
        }

        await using (stream.ConfigureAwait(false))
        {
            // SaveAsync adds no preamble: an initial U+FEFF belongs to the payload.
            using var reader = new StreamReader(stream, s_strictUtf8, detectEncodingFromByteOrderMarks: false);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sanitizes a caller-provided key into a filesystem-safe relative path.
    /// </summary>
    /// <remarks>
    /// Defense-in-depth against path traversal: explicit rejection of <c>..</c> segments,
    /// empty segments (<c>//</c>), absolute-rooted paths, and drive-letter prefixes. The
    /// underlying <see cref="FileSystemObjectStore"/> is expected to enforce the same
    /// boundaries, but duplicating them here keeps the MarketData-specific contract from
    /// silently weakening if the upstream library changes behavior.
    /// </remarks>
    private static string SanitizeKey(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        // Reject obvious root-escape patterns before character-level scrubbing. A leading
        // "/" or "\" or "C:" would produce a key that FileSystemObjectStore might interpret
        // as absolute.
        if (key.StartsWith('/') || key.StartsWith('\\'))
        {
            throw new ArgumentException(
                $"Key cannot begin with a path separator (got '{key}').",
                nameof(key));
        }

        if (key.Length >= 2 && key[1] == ':')
        {
            throw new ArgumentException(
                $"Key cannot contain a drive-letter prefix (got '{key}').",
                nameof(key));
        }

        // Split on either slash so the ".." check catches both POSIX and Windows conventions.
        var segments = key.Split(['/', '\\'], StringSplitOptions.None);
        foreach (var segment in segments)
        {
            if (segment is ".." or ".")
            {
                throw new ArgumentException(
                    $"Key segment '{segment}' is not allowed (path-traversal defense).",
                    nameof(key));
            }

            if (string.IsNullOrEmpty(segment))
            {
                throw new ArgumentException(
                    $"Key cannot contain empty segments (got '{key}').",
                    nameof(key));
            }
        }

        var encoded = new StringBuilder();
        for (var i = 0; i < key.Length; i++)
        {
            // Bound each physical component without losing logical separator identity.
            if (i > 0 && i % 48 == 0)
            {
                encoded.Append('/');
            }

            encoded.Append(((int)key[i]).ToString("x4", CultureInfo.InvariantCulture));
        }

        return encoded.ToString();
    }
}
