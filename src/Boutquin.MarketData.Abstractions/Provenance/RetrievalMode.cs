// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

namespace Boutquin.MarketData.Abstractions.Provenance;

/// <summary>
/// Closed set describing how a data record was obtained for provenance purposes.
/// </summary>
/// <remarks>
/// <para>
/// Attached to every <see cref="Results.DataProvenance"/> so consumers can distinguish
/// live-fetched data from cached, persisted, or derived values. The mode is orthogonal
/// to <see cref="Results.FreshnessClass"/>: a cache hit can still carry
/// <see cref="Results.FreshnessClass.Live"/> if the upstream was just fetched, and an
/// API call can return <see cref="Results.FreshnessClass.Stale"/> data if the source
/// has not updated.
/// </para>
/// <para>
/// Append-only ordinal contract: members are assigned explicit values and MUST NOT be
/// reordered or removed.
/// </para>
/// </remarks>
public enum RetrievalMode
{
    /// <summary>
    /// Live fetch against the upstream provider.
    /// </summary>
    Api = 0,

    /// <summary>
    /// Served from the L1 in-memory cache.
    /// </summary>
    Cache = 1,

    /// <summary>
    /// Served from an L2 durable snapshot.
    /// </summary>
    Snapshot = 2,

    /// <summary>
    /// Computed from another envelope (e.g. a synthesised FX cross from two legs).
    /// </summary>
    Derived = 3,

    /// <summary>
    /// Produced by a synthetic test fixture — not from any real provider.
    /// </summary>
    Fixture = 4,

    /// <summary>
    /// Served by the time-series cache tier after a live adapter fetch that fully filled the
    /// requested range (every requested day fell in a gap and was fetched fresh).
    /// Distinct from <see cref="Api"/>, which is the legacy-path live-fetch marker; a time-series
    /// gap-fill that covered the whole request reports <see cref="Live"/>.
    /// </summary>
    Live = 5,

    /// <summary>
    /// Served by the time-series cache tier from a mix of cached coverage and a partial live
    /// gap-fill — the request spanned both already-covered dates and one or more gaps that were
    /// fetched from the adapter. Also carried when a gap-fill adapter dispatch failed
    /// after at least one gap was durably appended, so the returned range is
    /// known to be incompletely filled.
    /// </summary>
    Partial = 6,
}
