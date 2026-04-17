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
/// Persists raw API responses before parsing or normalization, enabling payload replay,
/// schema-drift debugging, and audit of exactly what the source returned.
/// </summary>
/// <remarks>
/// <para>
/// This is distinct from <see cref="ISnapshotStore"/>, which stores canonical (post-normalization)
/// snapshots. The raw document store captures the original payload — JSON, CSV, XML — keyed by
/// a caller-defined key that typically encodes source, endpoint, and date context.
/// </para>
/// <para>
/// Adapters should call <see cref="SaveAsync"/> immediately after a successful HTTP fetch
/// and before any parsing. This ensures that debugging a parse failure does not require
/// re-fetching from the remote source.
/// </para>
/// </remarks>
public interface IRawDocumentStore
{
    /// <summary>
    /// Persists a raw document payload.
    /// </summary>
    /// <param name="key">
    /// A unique key identifying this document (e.g., "tiingo/AAPL/2024-01-01_2024-12-31").
    /// Implementations MUST reject path-traversal segments (<c>..</c>), absolute-rooted
    /// keys (leading <c>/</c> or <c>\</c>), drive-letter prefixes, and any segment that
    /// escapes the implementation's storage root. Callers that receive untrusted input
    /// SHOULD still validate keys up-front; the interface-level defense is the defense
    /// of last resort, not the primary boundary.
    /// </param>
    /// <param name="content">The raw payload content to persist.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentException">
    /// Thrown by implementations when <paramref name="key"/> contains path-traversal segments
    /// or other disallowed constructs.
    /// </exception>
    Task SaveAsync(string key, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a previously saved raw document, or returns <see langword="null"/> if not found.
    /// </summary>
    /// <param name="key">The key used when the document was saved.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The raw payload content, or <see langword="null"/> if the key does not exist.</returns>
    Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default);
}
