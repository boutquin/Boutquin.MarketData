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

using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Storage.InMemory;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit;

public sealed class InMemoryFixingsStoreTests
{
    private static readonly BenchmarkName s_sofr = new("USD-SOFR");
    private static readonly BenchmarkName s_sonia = new("GBP-SONIA");

    [Fact]
    public void GetFixing_returns_seeded_value()
    {
        var store = new InMemoryFixingsStore(
        [
            (s_sofr, new DateOnly(2026, 1, 2), 0.0533m),
        ]);

        store.GetFixing(s_sofr, new DateOnly(2026, 1, 2)).Should().Be(0.0533m);
    }

    [Fact]
    public void GetFixing_returns_null_when_missing()
    {
        var store = new InMemoryFixingsStore([]);

        store.GetFixing(s_sofr, new DateOnly(2026, 1, 2)).Should().BeNull();
    }

    [Fact]
    public void TryGetFixing_returns_true_on_hit()
    {
        var store = new InMemoryFixingsStore(
        [
            (s_sofr, new DateOnly(2026, 1, 2), 0.0533m),
        ]);

        store.TryGetFixing(s_sofr, new DateOnly(2026, 1, 2), out var v).Should().BeTrue();
        v.Should().Be(0.0533m);
    }

    [Fact]
    public void TryGetFixing_returns_false_on_miss()
    {
        var store = new InMemoryFixingsStore([]);

        store.TryGetFixing(s_sofr, new DateOnly(2026, 1, 2), out var v).Should().BeFalse();
        v.Should().Be(0m);
    }

    [Fact]
    public void Duplicate_seed_entries_follow_last_in_wins()
    {
        var store = new InMemoryFixingsStore(
        [
            (s_sofr, new DateOnly(2026, 1, 2), 0.0500m),
            (s_sofr, new DateOnly(2026, 1, 2), 0.0533m),
        ]);

        store.GetFixing(s_sofr, new DateOnly(2026, 1, 2)).Should().Be(0.0533m);
    }

    [Fact]
    public void GetFixings_returns_range_inclusive_and_ordered()
    {
        var store = new InMemoryFixingsStore(
        [
            (s_sofr, new DateOnly(2026, 1, 2), 0.053m),
            (s_sofr, new DateOnly(2026, 1, 3), 0.054m),
            (s_sofr, new DateOnly(2026, 1, 6), 0.055m),
            (s_sonia, new DateOnly(2026, 1, 2), 0.049m),
        ]);

        var range = new DateRange(new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 5));
        var result = store.GetFixings(s_sofr, range);

        result.Should().HaveCount(2);
        result[0].Date.Should().Be(new DateOnly(2026, 1, 2));
        result[0].Value.Should().Be(0.053m);
        result[1].Date.Should().Be(new DateOnly(2026, 1, 3));
        result[1].Value.Should().Be(0.054m);
    }

    [Fact]
    public void GetFixings_returns_empty_when_benchmark_unknown()
    {
        var store = new InMemoryFixingsStore([]);

        store.GetFixings(s_sofr, new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)))
            .Should().BeEmpty();
    }

    [Fact]
    public void Constructor_rejects_null_seed()
    {
        var act = () => _ = new InMemoryFixingsStore(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
