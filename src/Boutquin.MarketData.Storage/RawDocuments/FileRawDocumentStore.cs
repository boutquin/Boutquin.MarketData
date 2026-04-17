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
/// Keys are sanitized using MarketData-specific policy: forward slashes become directory
/// separators, characters outside <c>[a-zA-Z0-9._-/]</c> are replaced with underscores.
/// Files are stored with a <c>.json</c> extension.
/// </para>
/// <para>
/// The underlying <see cref="FileSystemObjectStore"/> provides atomic writes
/// and hierarchical key support. The <see cref="IObjectStore"/> constructor overload
/// allows injecting <see cref="InMemoryObjectStore"/> for testing.
/// </para>
/// </remarks>
public sealed class FileRawDocumentStore : IRawDocumentStore
{
    private readonly IObjectStore _store;

    /// <summary>
    /// Initializes a new instance of <see cref="FileRawDocumentStore"/>
    /// backed by the file system at the specified directory.
    /// </summary>
    /// <param name="rootDirectory">
    /// Root directory for raw document storage.
    /// </param>
    public FileRawDocumentStore(string rootDirectory)
        => _store = new FileSystemObjectStore(rootDirectory);

    /// <summary>
    /// Initializes a new instance of <see cref="FileRawDocumentStore"/>
    /// using the provided <see cref="IObjectStore"/> implementation.
    /// </summary>
    /// <param name="store">The object store to delegate to.</param>
    public FileRawDocumentStore(IObjectStore store)
        => _store = store;

    /// <inheritdoc />
    public async Task SaveAsync(string key, string content, CancellationToken cancellationToken = default)
    {
        var storageKey = SanitizeKey(key) + ".json";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await using (stream.ConfigureAwait(false))
        {
            await _store.WriteAsync(storageKey, stream, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        var storageKey = SanitizeKey(key) + ".json";
        var stream = await _store.ReadAsync(storageKey, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            return null;
        }

        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);
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

        var chars = key.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c == '/' || c == '\\')
            {
                chars[i] = Path.DirectorySeparatorChar;
            }
            else if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
