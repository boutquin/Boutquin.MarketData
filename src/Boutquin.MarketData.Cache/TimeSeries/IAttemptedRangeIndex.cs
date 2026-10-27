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

using Boutquin.MarketData.Abstractions.Contracts;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Records the widest date range the pipeline has already <b>attempted</b> to fetch for each
/// dataset — distinct from the store's record coverage. A time-series store's coverage tracks the
/// min/max dates of records that <i>exist</i>; it cannot distinguish "never asked" from "asked and
/// the source had nothing" (a leading gap before a ticker's inception, or an internal source
/// outage). This index closes that gap: once the pipeline dispatches an adapter for a leading span
/// and the source clips it (returns records starting later), the empty leading span is remembered
/// as attempted so a subsequent pre-inception request subsumes with zero re-dispatch.
/// </summary>
/// <remarks>
/// Keyed by the dataset's string identity so it is record-type agnostic. Registered as a DI
/// singleton by <c>AddMarketDataKernel</c>; the default is an in-process implementation whose
/// lifetime matches the L1 cache tier (a leading-gap re-fetch after a process restart is a benign
/// one-time redundant dispatch, then re-memoised).
/// </remarks>
public interface IAttemptedRangeIndex
{
    /// <summary>
    /// The merged, disjoint set of inclusive <c>[From, To]</c> spans the pipeline has already
    /// <b>attempted</b> to fetch for <paramref name="datasetIdentity"/>, ascending by <c>From</c>;
    /// empty when nothing has been attempted. Any gap sub-span lying entirely within one of these
    /// spans is known-empty (already attempted and the source had nothing there) and must not be
    /// re-dispatched; a sub-span outside every attempted span (a genuinely newer/older request, e.g.
    /// a nightly extend-by-one-day suffix) has never been attempted and must be dispatched.
    /// </summary>
    /// <param name="datasetIdentity">The dataset's string identity (its dataset key's canonical form).</param>
    IReadOnlyList<(DateOnly From, DateOnly To)> GetAttemptedRanges(string datasetIdentity);

    /// <summary>
    /// Records that the pipeline attempted to fetch the inclusive range
    /// <c>[<paramref name="from"/>, <paramref name="to"/>]</c> for <paramref name="datasetIdentity"/>.
    /// Merged (unioned) into the stored set — it only ever widens the attempted coverage, matching the
    /// range the adapter was actually dispatched for (the source returned everything it had within it,
    /// so the whole span is henceforth known, whether it yielded records or was clipped/empty).
    /// </summary>
    /// <param name="datasetIdentity">The dataset's string identity.</param>
    /// <param name="from">The earliest requested date that was dispatched.</param>
    /// <param name="to">The latest requested date that was dispatched.</param>
    void RecordAttempt(string datasetIdentity, DateOnly from, DateOnly to);
}

/// <summary>
/// Default in-process <see cref="IAttemptedRangeIndex"/>. Entries are an L1-aligned accelerator:
/// capacity and expiry bound its lifetime, while durable recovery metadata remains the authority
/// for restart-safe known-empty coverage.
/// </summary>
public sealed class InMemoryAttemptedRangeIndex : IAttemptedRangeIndex
{
    private const int DefaultCapacity = 1_024;
    private static readonly TimeSpan s_defaultTtl = TimeSpan.FromHours(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _ranges = [];
    private readonly int _capacity;
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// Creates the default L1-aligned accelerator. Production composition should use the overload
    /// that receives the shared clock so expiry is deterministic in host tests.
    /// </summary>
    public InMemoryAttemptedRangeIndex()
        : this(DefaultCapacity, s_defaultTtl, static () => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>
    /// Creates a bounded, expiring accelerator using the supplied clock.
    /// </summary>
    public InMemoryAttemptedRangeIndex(int capacity, TimeSpan ttl, IClock clock)
        : this(capacity, ttl, () => (clock ?? throw new ArgumentNullException(nameof(clock))).UtcNow)
    {
    }

    private InMemoryAttemptedRangeIndex(int capacity, TimeSpan ttl, Func<DateTimeOffset> utcNow)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(utcNow);

        _capacity = capacity;
        _ttl = ttl;
        _utcNow = utcNow;
    }

    /// <inheritdoc />
    public IReadOnlyList<(DateOnly From, DateOnly To)> GetAttemptedRanges(string datasetIdentity)
    {
        ArgumentNullException.ThrowIfNull(datasetIdentity);

        lock (_gate)
        {
            RemoveExpired();
            if (!_ranges.TryGetValue(datasetIdentity, out var entry))
            {
                return [];
            }

            _ranges[datasetIdentity] = entry with { LastAccessUtc = _utcNow() };
            return Array.AsReadOnly(entry.Spans.ToArray());
        }
    }

    /// <inheritdoc />
    public void RecordAttempt(string datasetIdentity, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(datasetIdentity);
        var incoming = from <= to ? (From: from, To: to) : (From: to, To: from);

        lock (_gate)
        {
            RemoveExpired();
            var now = _utcNow();
            var spans = _ranges.TryGetValue(datasetIdentity, out var existing)
                ? Merge(existing.Spans, incoming)
                : [incoming];
            _ranges[datasetIdentity] = new Entry(spans, now, now + _ttl);

            while (_ranges.Count > _capacity)
            {
                var oldest = _ranges.MinBy(static pair => pair.Value.LastAccessUtc).Key;
                _ranges.Remove(oldest);
            }
        }
    }

    private void RemoveExpired()
    {
        var now = _utcNow();
        foreach (var key in _ranges
            .Where(pair => pair.Value.ExpiresAtUtc <= now)
            .Select(static pair => pair.Key)
            .ToList())
        {
            _ranges.Remove(key);
        }
    }

    // Union a new span into a merged, disjoint, ascending set, coalescing overlapping OR adjacent
    // spans (adjacency: previous.To + 1 >= next.From) so e.g. [2005..2009] then [2010..2020] fuse.
    private static IReadOnlyList<(DateOnly From, DateOnly To)> Merge(
        IReadOnlyList<(DateOnly From, DateOnly To)> existing,
        (DateOnly From, DateOnly To) incoming)
    {
        var all = new List<(DateOnly From, DateOnly To)>(existing.Count + 1);
        all.AddRange(existing);
        all.Add(incoming);
        all.Sort(static (a, b) => a.From.CompareTo(b.From));

        var merged = new List<(DateOnly From, DateOnly To)>(all.Count);
        var current = all[0];
        for (var i = 1; i < all.Count; i++)
        {
            var next = all[i];
            if (next.From.DayNumber <= current.To.DayNumber + 1)
            {
                // Overlapping or adjacent — extend the current span's upper bound.
                if (next.To > current.To)
                {
                    current = (current.From, next.To);
                }
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    private sealed record Entry(
        IReadOnlyList<(DateOnly From, DateOnly To)> Spans,
        DateTimeOffset LastAccessUtc,
        DateTimeOffset ExpiresAtUtc);
}
