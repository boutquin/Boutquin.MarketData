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
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.Abstractions.Results;

namespace Boutquin.MarketData.Cache.Policies;

/// <summary>
/// Evaluates whether a cached snapshot is fresh enough for reuse based on a configurable maximum age.
/// </summary>
/// <remarks>
/// <para>
/// Staleness is determined by comparing the snapshot's storage timestamp against the current
/// UTC time. A snapshot older than <see cref="MaxAge"/> is considered stale.
/// </para>
/// <para>
/// Default maximum age is 24 hours, suitable for end-of-day data workflows where snapshots
/// are refreshed daily. For intraday data, configure a shorter threshold.
/// </para>
/// </remarks>
public sealed class SnapshotStalenessPolicy
{
    private static readonly TimeSpan s_defaultMaxAge = TimeSpan.FromHours(24);

    /// <summary>
    /// Maximum age before a snapshot is considered stale.
    /// </summary>
    public TimeSpan MaxAge { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="SnapshotStalenessPolicy"/>.
    /// </summary>
    /// <param name="maxAge">Maximum acceptable age for cached snapshots. Defaults to 24 hours if <see langword="null"/>.</param>
    public SnapshotStalenessPolicy(TimeSpan? maxAge = null)
    {
        MaxAge = maxAge ?? s_defaultMaxAge;
    }

    /// <summary>
    /// Determines whether the snapshot stored at the given timestamp is stale.
    /// </summary>
    /// <param name="storedAtUtc">The UTC timestamp when the snapshot was stored.</param>
    /// <returns><see langword="true"/> if the snapshot's age exceeds <see cref="MaxAge"/>.</returns>
    public bool IsStale(DateTimeOffset storedAtUtc) =>
        DateTimeOffset.UtcNow - storedAtUtc > MaxAge;

    /// <summary>
    /// Checks staleness and returns a <see cref="DataIssue"/> warning if the snapshot is stale,
    /// or <see langword="null"/> if fresh.
    /// </summary>
    /// <param name="snapshotKey">The key identifying the snapshot, included in the warning message.</param>
    /// <param name="storedAtUtc">The UTC timestamp when the snapshot was stored.</param>
    /// <returns>A warning issue if stale; otherwise <see langword="null"/>.</returns>
    public DataIssue? CheckStaleness(string snapshotKey, DateTimeOffset storedAtUtc)
    {
        var age = DateTimeOffset.UtcNow - storedAtUtc;
        if (age <= MaxAge)
        {
            return null;
        }

        var ageText = age.TotalHours >= 1
            ? $"{age.TotalHours:F1} hours"
            : $"{age.TotalMinutes:F0} minutes";

        return new DataIssue(
            IssueCode.SnapshotStale,
            IssueSeverity.Warning,
            $"Snapshot '{snapshotKey}' is {ageText} old (threshold: {MaxAge.TotalHours:F1} hours).");
    }
}
