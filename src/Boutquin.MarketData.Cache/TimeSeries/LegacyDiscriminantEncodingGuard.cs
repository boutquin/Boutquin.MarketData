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

using Boutquin.MarketData.Abstractions.Exceptions;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Fail-fast detector for a stored dataset key written under the <b>pre-canonicalisation</b>
/// discriminant encoding — the on-disk migration guard for the body-discriminant fan-outs.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Builds before the discriminant canonicalisation projected a
/// fan-out key column straight off the record body, so a source publishing a non-canonical spelling
/// (a factor archive whose CSV header is <c>"ST_Rev"</c>, a feed emitting <c>" 10y "</c>) persisted
/// under that raw spelling. Current builds project the canonical form for the same series. Upgrading
/// therefore changes the key a series is written under, and the two keys are not interchangeable: the
/// legacy key is never re-fetched, the canonical key reports the whole history as a gap and re-fetches
/// it, and the discriminant-blind membership filter merges both — so every date is returned twice.</para>
/// <para><b>Why the check is exact.</b> The canonicalisation is idempotent, so every key this build
/// writes satisfies <c>Canonical(k) == k</c>. A stored key that fails that equality therefore
/// provably predates the change (no false positives), and every stored key the current encoding
/// would rewrite provably fails it (no false negatives). The guard is the precise detector for
/// "this key would be re-keyed by this build".</para>
/// <para><b>Why it fails fast rather than aliasing.</b> Consulting the raw-spelling key on the read
/// path does not remove the hazard: any re-dispatch over the historical range (a stale tail, a
/// widened request, a cleared attempted-range index) appends canonical-key rows for dates the legacy
/// key already holds, and the cross-key merge then double-counts them. Reporting the legacy key
/// before any append is the only disposition that cannot duplicate a series. This mirrors the
/// delete-and-re-ingest contract the store tier already publishes for the Boutquin.Storage 1.x → 2.0
/// wire break.</para>
/// </remarks>
internal static class LegacyDiscriminantEncodingGuard
{
    /// <summary>
    /// The documentation anchor surfaced on a fail-fast, so the operator can correlate the break
    /// with the migration playbook.
    /// </summary>
    internal const string DocsAnchor =
        "docs/time-series-cache.md#migrating-a-store-written-before-the-canonicalisation";

    /// <summary>
    /// Verifies that a stored key's discriminant is already in the encoding this build writes.
    /// </summary>
    /// <param name="recordType">The record type whose store is being read (for the message).</param>
    /// <param name="column">The discriminant column name (for the message).</param>
    /// <param name="stored">The discriminant value as read from the stored dataset key.</param>
    /// <param name="canonical">
    /// The canonical form of <paramref name="stored"/> — the value this build would project for the
    /// same series.
    /// </param>
    /// <exception cref="MarketDataStorageException">
    /// <paramref name="stored"/> differs from <paramref name="canonical"/>, i.e. the key was written
    /// by a build that predates the discriminant canonicalisation. The store directory must be
    /// cleared and re-ingested; see <see cref="DocsAnchor"/>.
    /// </exception>
    internal static void EnsureCanonicalDiscriminant(
        string recordType, string column, string stored, string canonical)
    {
        if (string.Equals(stored, canonical, StringComparison.Ordinal))
        {
            return;
        }

        throw new MarketDataStorageException(
            $"Time-series store for record type '{recordType}' holds a dataset key whose {column} "
            + $"discriminant is \"{stored}\", which predates the discriminant canonicalisation — this "
            + $"build writes \"{canonical}\" for the same series. Reading both would merge one logical "
            + "series from two keys and re-fetch it under the new one, double-counting every shared "
            + $"date. See {DocsAnchor} for the delete-and-re-ingest migration.");
    }
}
