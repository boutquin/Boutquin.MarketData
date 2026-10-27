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

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// AC-3 — <c>AddTimeSeriesStore&lt;TDatasetKey, TRecord&gt;</c> registers a resolvable store in
/// the registry, and the <c>configure</c> callback overrides the builder defaults.
/// </summary>
public sealed class AddTimeSeriesStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "mktdata-addts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void AddTimeSeriesStore_RegistersResolvableStore()
    {
        var services = new ServiceCollection();

        services.AddTimeSeriesStore<PriceBarDatasetKey, Bar>(b => b.WithSegmentRoot(_root));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ITimeSeriesStoreRegistry>();

        var store = registry.Resolve<PriceBarDatasetKey, Bar>();

        store.Should().NotBeNull();
    }

    [Fact]
    public async Task AddTimeSeriesStore_ConfigureCallback_OverridesDefaults_StoreStillRoundTrips()
    {
        var services = new ServiceCollection();

        // Override the columnar default with the row opt-out via the configure callback.
        services.AddTimeSeriesStore<PriceBarDatasetKey, Bar>(b => b
            .WithSegmentRoot(_root)
            .UseRowFormat());

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>();

        store.Should().NotBeNull();

        var key = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", null);
        var bar = new Bar(new DateOnly(2024, 1, 2), 100m, 101m, 99m, 100.5m, 100.5m, 1_000_000L);
        await store!.AppendAsync(key, [bar]);

        var read = new List<Bar>();
        await foreach (var r in store.GetRangeAsync(key, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3)))
        {
            read.Add(r);
        }

        read.Should().ContainSingle().Which.Date.Should().Be(new DateOnly(2024, 1, 2));
    }

    [Fact]
    public async Task AddTimeSeriesStore_RegistersResolvableEngine_BackedBySameData()
    {
        var services = new ServiceCollection();

        // Row format so the engine supports snapshots (columnar rejects AcquireSnapshotAsync).
        services.AddTimeSeriesStore<PriceBarDatasetKey, Bar>(b => b
            .WithSegmentRoot(_root)
            .UseRowFormat());

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ITimeSeriesStoreRegistry>();

        // M-4.5 anti-inertness pin: ResolveEngine must return the SAME engine the store reads
        // through. Append via the store, then read back the row through a snapshot acquired on the
        // resolved engine — if the engine were a different/empty instance, the snapshot read would
        // see nothing.
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var key = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", null);
        var bar = new Bar(new DateOnly(2024, 1, 2), 100m, 101m, 99m, 100.5m, 100.5m, 1_000_000L);
        await store.AppendAsync(key, [bar]);

        var engine = registry.ResolveEngine<PriceBarDatasetKey, Bar>();
        engine.Should().NotBeNull();

        await using var snapshot = await engine.AcquireSnapshotAsync();
        var read = new List<Bar>();
        await foreach (var kv in snapshot.GetRangeAsync(
            new(key, new DateOnly(2024, 1, 1)), new(key, new DateOnly(2024, 1, 3))))
        {
            read.Add(kv.Value);
        }

        read.Should().ContainSingle().Which.Date.Should().Be(new DateOnly(2024, 1, 2));
    }
}
