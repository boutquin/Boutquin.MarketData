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

using Boutquin.MarketData.Abstractions;
using Boutquin.MarketData.Abstractions.Primitives;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;

namespace Boutquin.MarketData.Storage.InMemory;

/// <summary>
/// In-memory, read-only implementation of <see cref="IFixingsStore"/> backed by
/// per-benchmark sorted-date dictionaries.
/// </summary>
/// <remarks>
/// <para>
/// Intended for unit tests, fixtures, and single-process curve construction. For
/// durable persistence or multi-process scenarios, implement <see cref="IFixingsStore"/>
/// against a storage engine such as <c>Boutquin.Storage.Infrastructure</c> or a
/// database.
/// </para>
/// <para>
/// The seed stream is consumed eagerly; duplicate
/// <c>(benchmark, date)</c> tuples follow a last-in-wins rule silently, matching the
/// historical Boutquin.Curves behaviour.
/// </para>
/// <para>
/// <strong>Thread safety.</strong> The instance is immutable once constructed, so
/// concurrent reads are safe without external synchronization. There is no mutation
/// surface in v1.
/// </para>
/// </remarks>
public sealed class InMemoryFixingsStore : IFixingsStore
{
    private readonly Dictionary<BenchmarkName, SortedDictionary<DateOnly, decimal>> _data = [];

    /// <summary>
    /// Initializes a new <see cref="InMemoryFixingsStore"/> seeded from the given
    /// tuple stream.
    /// </summary>
    /// <param name="seed">
    /// Sequence of <c>(benchmark, date, value)</c> tuples. Duplicates for the same
    /// <c>(benchmark, date)</c> key follow a last-in-wins rule.
    /// </param>
    public InMemoryFixingsStore(IEnumerable<(BenchmarkName Benchmark, DateOnly Date, decimal Value)> seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        foreach (var (benchmark, date, value) in seed)
        {
            if (!_data.TryGetValue(benchmark, out var series))
            {
                series = [];
                _data[benchmark] = series;
            }

            series[date] = value;
        }
    }

    /// <inheritdoc />
    public decimal? GetFixing(BenchmarkName benchmark, DateOnly fixingDate)
    {
        if (_data.TryGetValue(benchmark, out var series) &&
            series.TryGetValue(fixingDate, out var value))
        {
            return value;
        }

        return null;
    }

    /// <inheritdoc />
    public bool TryGetFixing(BenchmarkName benchmark, DateOnly fixingDate, out decimal value)
    {
        if (_data.TryGetValue(benchmark, out var series) &&
            series.TryGetValue(fixingDate, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<TimeSeriesPoint<decimal>> GetFixings(BenchmarkName benchmark, DateRange range)
    {
        ArgumentNullException.ThrowIfNull(range);

        if (!_data.TryGetValue(benchmark, out var series))
        {
            return [];
        }

        var result = new List<TimeSeriesPoint<decimal>>();
        foreach (var (date, value) in series)
        {
            if (date < range.From)
            {
                continue;
            }

            if (date > range.To)
            {
                break;
            }

            result.Add(new TimeSeriesPoint<decimal>(date, value));
        }

        return result;
    }
}
