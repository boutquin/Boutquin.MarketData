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

using System.Reflection;

using Boutquin.MarketData.Abstractions.Enums;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Observability;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;

using Boutquin.Storage.Domain.Enums;
using Boutquin.Storage.Infrastructure.TimeSeries;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// AC-2 — the fluent <see cref="TimeSeriesStoreBuilder{TDatasetKey, TRecord}"/> sets the right
/// configuration, selects the right segment format, enforces the WiscKey⇒row-format guardrail
/// with the exact §2.8.1 message, translates the compaction strategy, produces a non-null bloom
/// factory, and builds a live <see cref="LsmTimeSeriesStore{TDatasetKey, TRecord}"/>.
/// </summary>
public sealed class TimeSeriesStoreBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "mktdata-tsbuilder-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar> NewBuilder()
        => new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar")
            .WithSegmentRoot(_root);

    [Fact]
    public void UseColumnarFormat_SelectsColumnarSegmentFormat()
    {
        var b = NewBuilder().UseColumnarFormat();

        b.SegmentFormat.Should().Be(TimeSeriesSegmentFormat.Columnar);
    }

    [Fact]
    public void UseRowFormat_SelectsRowSegmentFormat()
    {
        var b = NewBuilder().UseRowFormat();

        b.SegmentFormat.Should().Be(TimeSeriesSegmentFormat.Row);
    }

    [Fact]
    public void WithCompactionStrategy_IsRecorded()
    {
        var b = NewBuilder().WithCompactionStrategy(TimeSeriesCompactionStrategy.Leveled);

        b.CompactionStrategy.Should().Be(TimeSeriesCompactionStrategy.Leveled);
        b.ResolveCompactionStrategy().Should().NotBeNull();
    }

    [Fact]
    public void WithQuarantineBehavior_IsRecorded()
    {
        var b = NewBuilder().WithQuarantineBehavior(QuarantineBehavior.FailQuery);

        b.QuarantineBehavior.Should().Be(QuarantineBehavior.FailQuery);
    }

    [Fact]
    public void Configuration_RejectsUndefinedEnums()
    {
        var compaction = () => NewBuilder().WithCompactionStrategy((TimeSeriesCompactionStrategy)999);
        var quarantine = () => NewBuilder().WithQuarantineBehavior((QuarantineBehavior)999);

        compaction.Should().Throw<ArgumentOutOfRangeException>();
        quarantine.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ResolveSegmentFolder_Default_IsAbsoluteAndIndependentOfCurrentDirectory()
    {
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar");

        var resolved = builder.ResolveSegmentFolder();

        Path.IsPathFullyQualified(resolved).Should().BeTrue();
        resolved.Should().Be(Path.Combine(AppContext.BaseDirectory, ".marketdata-timeseries", "Bar"));
    }

    [Fact]
    public void WithBloomFilter_ProducesNonNullFactory()
    {
        var b = NewBuilder().WithBloomFilter(new TimeSeriesBloomFilterOptions
        {
            ExpectedItems = 4096,
            FalsePositiveRate = 0.02,
        });

        var filter = b.BuildBloomFilter();

        filter.Should().NotBeNull();
    }

    [Fact]
    public void UseValueLog_WithoutUseRowFormat_ThrowsInvalidOperationException()
    {
        var b = NewBuilder().UseColumnarFormat();

        var act = () => b.UseValueLog(o => { });

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("WiscKey dispatch requires UseRowFormat — columnar segments reject non-inline entries.");
    }

    [Fact]
    public void UseValueLog_AfterUseRowFormat_DoesNotThrow()
    {
        var b = NewBuilder().UseRowFormat();

        var act = () => b.UseValueLog(o => { });

        act.Should().NotThrow();
    }

    [Fact]
    public void Store_only_builder_requires_ownership_bearing_replacement()
    {
        var obsolete = typeof(TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>)
            .GetMethod("BuildAsync")!.GetCustomAttribute<ObsoleteAttribute>();
        obsolete.Should().NotBeNull("store-only construction loses the engine disposal owner");
        obsolete!.IsError.Should().BeTrue();
        obsolete.Message.Should().Contain("BuildStackAsync");
    }

    [Fact]
    public async Task BuildStackAsync_Columnar_YieldsLiveStoreWithDisposalOwner()
    {
        var stack = await NewBuilder().UseColumnarFormat().BuildStackAsync();
        await using var owner = (IAsyncDisposable)stack.Engine;
        var store = stack.Store;

        store.Should().NotBeNull();
        store.Should().BeOfType<LsmTimeSeriesStore<PriceBarDatasetKey, Bar>>();

        // Live, not inert: a round-trip append+read returns the row.
        var key = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", null);
        var bar = new Bar(new DateOnly(2024, 1, 2), 100m, 101m, 99m, 100.5m, 100.5m, 1_000_000L);
        await store.AppendAsync(key, [bar]);

        var read = new List<Bar>();
        await foreach (var r in store.GetRangeAsync(key, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3)))
        {
            read.Add(r);
        }

        read.Should().ContainSingle().Which.Date.Should().Be(new DateOnly(2024, 1, 2));
    }
}
