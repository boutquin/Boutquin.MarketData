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

namespace Boutquin.MarketData.Orchestration.Pipeline;

/// <summary>
/// Configurable options controlling the data pipeline's L1 cache lifetime and partition
/// failure-handling strategy.
/// </summary>
/// <remarks>
/// <para>
/// Binding: supply via <c>IOptions&lt;DataPipelineOptions&gt;</c> through the standard .NET
/// options pattern. Defaults are tuned for end-of-day workflows (1-hour L1 TTL, continue on
/// partition failure) where occasional gaps are preferable to an entire batch failing.
/// </para>
/// <para>
/// Intraday tuning: shorten <see cref="L1CacheTtl"/> when the pipeline is fed by a scheduler
/// that polls faster than the cache window — a 1-hour TTL will serve stale bars to a 15-minute
/// poller. Strict-consistency consumers should set <see cref="ContinueOnPartitionFailure"/> to
/// <see langword="false"/> so a partial batch never reaches downstream analytics.
/// </para>
/// </remarks>
public sealed class DataPipelineOptions
{
    /// <summary>
    /// How long a completed envelope remains in the L1 in-memory cache before the next request
    /// for the same cache key falls through to L2 or the live adapter chain. Applies per cache
    /// entry, starting from the moment the entry is written.
    /// </summary>
    /// <remarks>
    /// Default: 1 hour — fits daily bar workflows that re-run once per day with the same inputs.
    /// Lower this for intraday polling; raise it for historical backfills where the underlying
    /// data is known to be immutable.
    /// </remarks>
    public TimeSpan L1CacheTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// When <see langword="true"/> (default), a single partition failure is recorded as a
    /// <c>PARTITION_FAILED</c> data issue and the pipeline continues with the remaining
    /// partitions, returning a partial result. When <see langword="false"/>, the pipeline stops
    /// at the first partition failure and returns what it has gathered so far.
    /// </summary>
    /// <remarks>
    /// Set to <see langword="false"/> for workflows that require all-or-nothing semantics (for
    /// example regulatory filings). Leave <see langword="true"/> for analytics backfills where
    /// "most symbols" is preferable to "no symbols" on a transient outage.
    /// </remarks>
    public bool ContinueOnPartitionFailure { get; set; } = true;
}
