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

using Boutquin.MarketData.Abstractions.Results;

namespace Boutquin.MarketData.Storage.Snapshots;

/// <summary>
/// Immutable metadata stored alongside a snapshot payload, carrying the write timestamp,
/// provenance chain, and data-quality issues required for staleness policies and audit replay.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline role: answers the questions "when was this snapshot produced?", "which adapters
/// contributed?", and "what data-quality issues did the pipeline observe?" without having to
/// re-fetch from the source. Stored as a header inside <see cref="SnapshotEnvelope{T}"/>.
/// </para>
/// <para>
/// Immutability: this is a record type with read-only collections by contract. Instances are
/// created once when a snapshot is written and must not be mutated afterwards — downstream
/// readers (staleness policy, audit replay) assume the value is stable for the lifetime of the
/// snapshot. Callers rebuilding a snapshot must construct a new instance rather than casting
/// the collections to mutable types.
/// </para>
/// <para>
/// Timestamp semantics: <see cref="StoredAtUtc"/> is the write time at the snapshot store, not
/// the data observation time. A snapshot may contain bars dated 2025-12-31 but have
/// <c>StoredAtUtc</c> of 2026-04-14 if that was when the pipeline fetched and persisted them.
/// Staleness checks compare against <see cref="StoredAtUtc"/>; coverage analysis compares
/// against the record timestamps inside the payload.
/// </para>
/// </remarks>
/// <param name="StoredAtUtc">Wall-clock UTC time at which the enclosing snapshot was written. Used by <c>SnapshotStalenessPolicy</c> to decide whether the snapshot can serve a read or must be refreshed.</param>
/// <param name="Provenance">Ordered trail of adapter-reported provenance entries. Each entry identifies the provider, retrieval mode, licensing, and normalization that contributed to the payload, in the order they participated during fetch.</param>
/// <param name="Issues">Data-quality issues captured during fetch (rollback warnings, coverage gaps, upstream rate-limit events). Surfaced to consumers so the caller can decide whether the snapshot is still actionable despite known defects.</param>
public sealed record SnapshotMetadata(
    DateTimeOffset StoredAtUtc,
    IReadOnlyList<DataProvenance> Provenance,
    IReadOnlyList<DataIssue> Issues)
{
    /// <summary>
    /// Ordered provenance entries, copied on construction and initialization and exposed read-only.
    /// </summary>
    public IReadOnlyList<DataProvenance> Provenance
    {
        get;
        init => field = CopyAuditCollection(value);
    } = CopyAuditCollection(Provenance);

    /// <summary>
    /// Data-quality issues, copied on construction and initialization and exposed read-only.
    /// </summary>
    public IReadOnlyList<DataIssue> Issues
    {
        get;
        init => field = CopyAuditCollection(value);
    } = CopyAuditCollection(Issues);

    /// <summary>
    /// Source-authoritative coverage accepted for the stored payload when the
    /// snapshot writer recorded it. Older snapshots omit this additive field; readers must
    /// retain their dataset-specific reconstruction policy for that legacy shape.
    /// </summary>
    public DataCoverage? Coverage { get; init; }

    private static IReadOnlyList<T> CopyAuditCollection<T>(IReadOnlyList<T> values)
    {
        // Preserve missing-collection detection at the serializer boundary, including legacy JSON.
        if (values is null)
        {
            return null!;
        }

        // An empty array has no mutable elements and preserves empty-record equality.
        return values.Count == 0 ? Array.Empty<T>() : Array.AsReadOnly(values.ToArray());
    }
}
