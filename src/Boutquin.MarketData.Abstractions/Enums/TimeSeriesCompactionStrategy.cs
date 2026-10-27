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

namespace Boutquin.MarketData.Abstractions.Enums;

/// <summary>
/// Selects the LSM compaction schedule a time-series store applies as on-disk segments
/// accumulate. Maps one-to-one onto a Boutquin.Storage <c>ICompactionStrategy</c> in the
/// DI builder.
/// </summary>
/// <remarks>
/// <para><b>Default rationale (DDIA 2e Ch. 3 p. 124).</b> MarketData's append-mostly,
/// rarely-mutated time-series workload is the textbook case for <see cref="SizeTiered"/> —
/// Cassandra-style size-tier bucketing minimizes write amplification on a stream of
/// immutable dated rows. <see cref="Leveled"/> trades higher write amplification for tighter
/// read amplification and is the right choice only when point reads dominate. <see cref="Full"/>
/// merges every segment on each compaction; it is correct only for tiny datasets where the
/// whole store fits a single segment.</para>
/// </remarks>
public enum TimeSeriesCompactionStrategy
{
    /// <summary>
    /// Cassandra-style size-tiered compaction — merges segments of similar size into the next
    /// tier. The library default; matches MarketData's append-mostly workload.
    /// </summary>
    SizeTiered,

    /// <summary>
    /// LevelDB-style leveled compaction — maintains size-bounded levels with non-overlapping
    /// key ranges per level. Lower read amplification at the cost of higher write amplification.
    /// </summary>
    Leveled,

    /// <summary>
    /// Threshold-driven whole-store merge — every segment is rewritten into one on each
    /// compaction. Useful only for tiny datasets.
    /// </summary>
    Full,
}
