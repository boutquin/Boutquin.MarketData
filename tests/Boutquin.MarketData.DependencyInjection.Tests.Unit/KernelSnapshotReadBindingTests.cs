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
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Abstractions.Snapshots;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.Orchestration.Snapshots;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// M-4.5-fix — the production read path is LIVE through the kernel. <c>AddMarketDataKernel</c>
/// registers a real <see cref="ISnapshotReadBinding{TRecord}"/> for every store, so a snapshot
/// acquired from the pipeline reads through the production <c>LsmSnapshotReadBinding</c> resolved
/// from the kernel's own DI container — not a test double. These tests are the fidelity reopen
/// probe (round-6 PARTIAL-INERT finding) codified: they fail RED with
/// "No snapshot read binding is registered…" until the production binding is registered.
/// </summary>
public sealed class KernelSnapshotReadBindingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "mktdata-kernel-snap-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; the OS reaps the temp dir eventually.
        }
    }

    // The production-path round-trip pin (ROW opt-in). Build the REAL kernel (no hand-injected
    // binding); register ONE record in ROW format via the public AddTimeSeriesStore opt-in; append
    // records covering the requested range through the resolved store; then read them back through
    // pipeline.AcquireReadSnapshotAsync() -> ExecuteAsync<TRecord>. The binding that serves the read
    // is resolved from the kernel's IServiceProvider — proving the production LsmSnapshotReadBinding
    // is registered and live, not a test double.
    [Fact]
    public async Task AddMarketDataKernel_RowSnapshotRead_RoundTripsThroughProductionBinding()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        // Explicit row-format opt-in for this record (the per-store UseRowFormat() opt-in the spec
        // sanctions; the operator decision on the DEFAULT format stays open). Re-registering a
        // (TDatasetKey, TRecord) store is last-writer-wins in the registry, so this row store shadows
        // the kernel's columnar default for ScalarObservation.
        services.AddTimeSeriesStore<ScalarObservationDatasetKey, ScalarObservation>(b => b
            .WithSegmentRoot(Path.Combine(_root, "row-scalar"))
            .UseRowFormat());

        using var provider = services.BuildServiceProvider();

        var env = await AppendAndSnapshotReadAsync(provider);

        env.Payload.Select(r => r.Value).Should().Equal(
            new[] { 4.21m, 4.22m, 4.23m },
            "the row-format snapshot read round-trips through the production binding");
    }

    // The production-path round-trip pin (DEFAULT columnar). The kernel registers ScalarObservation as
    // a COLUMNAR store by default; the snapshot read path serves it through the SAME production
    // LsmSnapshotReadBinding. (Empirically, ILsmStorageEngine.AcquireSnapshotAsync is format-agnostic
    // at the engine layer — the columnar restriction is on the value-log/WiscKey write path, not on
    // snapshot acquisition — so the shipped default records are snapshot-readable as-is.) No row
    // opt-in, no change to the default registration.
    [Fact]
    public async Task AddMarketDataKernel_DefaultColumnarSnapshotRead_RoundTripsThroughProductionBinding()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        using var provider = services.BuildServiceProvider();

        var env = await AppendAndSnapshotReadAsync(provider);

        env.Payload.Select(r => r.Value).Should().Equal(
            new[] { 4.21m, 4.22m, 4.23m },
            "the default columnar snapshot read round-trips through the production binding too");
    }

    // Gap fail-fast pin through the production path: when the requested range is NOT fully covered,
    // the production binding reports the gap and DataSnapshot throws SnapshotGapException — never
    // dispatching an adapter. Proves the production binding's gap detection is live end-to-end.
    [Fact]
    public async Task AddMarketDataKernel_SnapshotRead_WhenRangeHasGap_ThrowsSnapshotGapException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<ScalarObservationDatasetKey, ScalarObservation>()!;
        var key = new ScalarObservationDatasetKey("default", "DGS10");
        // Days 1 and 3 present; day 2 missing — an interior gap over the requested [1,3].
        await store.AppendAsync(key, [
            new ScalarObservation(new DateOnly(2024, 1, 1), 4.21m, "decimal"),
            new ScalarObservation(new DateOnly(2024, 1, 3), 4.23m, "decimal"),
        ]);

        var pipeline = provider.GetRequiredService<IDataPipeline>();
        await using var snap = await pipeline.AcquireReadSnapshotAsync();

        var request = new EconomicSeriesRequest(
            new EconomicSeriesId("DGS10"),
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3)));

        var act = async () => await snap.ExecuteAsync<ScalarObservation>(request);

        await act.Should().ThrowAsync<SnapshotGapException>(
            "the production binding reports the gap and the snapshot path fails fast, never dispatching an adapter");
    }

    // Weekend-spanning FULLY-COVERED read must NOT raise a gap (F-L1L6-01). Business-day data has no
    // weekend rows: a fully-covered business-week read that straddles a Sat/Sun legitimately returns
    // fewer records than the inclusive calendar-day span. The gap classifier must measure coverage
    // against the BUSINESS calendar, not the raw calendar-day count. Corpus: Thu 2024-01-04, Fri 01-05,
    // Mon 01-08, Tue 01-09 (Sat 01-06 / Sun 01-07 have no rows); requested range [01-04 .. 01-09].
    [Fact]
    public async Task AddMarketDataKernel_SnapshotRead_WeekendSpanningFullyCovered_DoesNotRaiseGap()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<ScalarObservationDatasetKey, ScalarObservation>()!;
        var key = new ScalarObservationDatasetKey("default", "DGS10");
        await store.AppendAsync(key, [
            new ScalarObservation(new DateOnly(2024, 1, 4), 4.24m, "decimal"), // Thu
            new ScalarObservation(new DateOnly(2024, 1, 5), 4.25m, "decimal"), // Fri
            // Sat 2024-01-06 and Sun 2024-01-07 have NO rows — business-day data.
            new ScalarObservation(new DateOnly(2024, 1, 8), 4.28m, "decimal"), // Mon
            new ScalarObservation(new DateOnly(2024, 1, 9), 4.29m, "decimal"), // Tue
        ]);

        var pipeline = provider.GetRequiredService<IDataPipeline>();
        await using var snap = await pipeline.AcquireReadSnapshotAsync();

        var request = new EconomicSeriesRequest(
            new EconomicSeriesId("DGS10"),
            new DateRange(new DateOnly(2024, 1, 4), new DateOnly(2024, 1, 9)));

        var env = await snap.ExecuteAsync<ScalarObservation>(request);

        env.Payload.Select(r => r.Value).Should().Equal(
            new[] { 4.24m, 4.25m, 4.28m, 4.29m },
            "a fully-covered business-week read spanning a weekend is NOT a gap");
    }

    // Genuine interior BUSINESS-day gap adjacent to a weekend still fails fast (AC-16 preserved).
    // Corpus: Thu 2024-01-04 and Mon 2024-01-08 present; Fri 2024-01-05 (a business day) MISSING;
    // requested [01-04 .. 01-08]. The weekend (Sat 01-06 / Sun 01-07) is legitimately empty, but the
    // missing Friday is a real uncovered business day — the snapshot must raise SnapshotGapException.
    [Fact]
    public async Task AddMarketDataKernel_SnapshotRead_WhenInteriorBusinessDayMissing_ThrowsSnapshotGapException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<ScalarObservationDatasetKey, ScalarObservation>()!;
        var key = new ScalarObservationDatasetKey("default", "DGS10");
        await store.AppendAsync(key, [
            new ScalarObservation(new DateOnly(2024, 1, 4), 4.24m, "decimal"), // Thu present
            // Fri 2024-01-05 MISSING — a genuine uncovered business day.
            new ScalarObservation(new DateOnly(2024, 1, 8), 4.28m, "decimal"), // Mon present
        ]);

        var pipeline = provider.GetRequiredService<IDataPipeline>();
        await using var snap = await pipeline.AcquireReadSnapshotAsync();

        var request = new EconomicSeriesRequest(
            new EconomicSeriesId("DGS10"),
            new DateRange(new DateOnly(2024, 1, 4), new DateOnly(2024, 1, 8)));

        var act = async () => await snap.ExecuteAsync<ScalarObservation>(request);

        await act.Should().ThrowAsync<SnapshotGapException>(
            "a missing interior business day is a real gap even when the range also spans a weekend");
    }

    // Gap A snapshot-read sibling (run 2026-07-07-1539-b90683, round 3): a MULTI-symbol
    // PriceHistoryRequest snapshot read must return EACH symbol's own bars, not just Symbols[0]'s.
    // LsmSnapshotReadBinding.BuildDatasetKey builds a single dataset key from the whole request; before
    // the partition-first fix it keyed on Symbols[0] and silently returned only that symbol's bars.
    [Fact]
    public async Task AddMarketDataKernel_MultiSymbolSnapshotRead_ReturnsEachSymbolsOwnBars()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<PriceBarDatasetKey, Bar>()!;

        var day = new DateOnly(2024, 1, 2); // Tuesday — a business day.
        var aaplKey = new PriceBarDatasetKey("default", DataFrequency.Daily, "AAPL", null);
        var msftKey = new PriceBarDatasetKey("default", DataFrequency.Daily, "MSFT", null);
        await store.AppendAsync(aaplKey, [new Bar(day, 100m, 100m, 100m, 100m, 100m, 1_000)]);
        await store.AppendAsync(msftKey, [new Bar(day, 101m, 101m, 101m, 101m, 101m, 1_000)]);

        var pipeline = provider.GetRequiredService<IDataPipeline>();
        await using var snap = await pipeline.AcquireReadSnapshotAsync();

        var request = new PriceHistoryRequest(
            [new Symbol("AAPL"), new Symbol("MSFT")],
            new DateRange(day, day));

        var env = await snap.ExecuteAsync<Bar>(request);

        env.Payload.Select(b => b.Open).Should().BeEquivalentTo(
            new[] { 100m, 101m },
            "a multi-symbol snapshot read must return each symbol's own bars, not just Symbols[0]'s");
    }

    // Appends a 3-day covered series for ("default", "DGS10") through the resolved store, then reads
    // it back through pipeline.AcquireReadSnapshotAsync() -> ExecuteAsync<ScalarObservation>. The read
    // resolves the dataset key via the kernel's registered EconomicSeriesDatasetKeyBuilder("default")
    // and flows through the production LsmSnapshotReadBinding — never a test double.
    private static async Task<DataEnvelope<IReadOnlyList<ScalarObservation>>> AppendAndSnapshotReadAsync(
        IServiceProvider provider)
    {
        var store = provider.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<ScalarObservationDatasetKey, ScalarObservation>()!;
        var key = new ScalarObservationDatasetKey("default", "DGS10");
        await store.AppendAsync(key, [
            new ScalarObservation(new DateOnly(2024, 1, 1), 4.21m, "decimal"),
            new ScalarObservation(new DateOnly(2024, 1, 2), 4.22m, "decimal"),
            new ScalarObservation(new DateOnly(2024, 1, 3), 4.23m, "decimal"),
        ]);

        var pipeline = provider.GetRequiredService<IDataPipeline>();
        await using var snap = await pipeline.AcquireReadSnapshotAsync();

        var request = new EconomicSeriesRequest(
            new EconomicSeriesId("DGS10"),
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3)));

        return await snap.ExecuteAsync<ScalarObservation>(request);
    }

    // Anti-inertness probe (the round-6 lesson): resolve ISnapshotReadBinding<TRecord> from the REAL
    // kernel's IServiceProvider and prove it is present AND a non-test src/ type. The prior M-4.5
    // report claimed "reachable through production DI" while the only binding was a test double; this
    // test makes that claim empirically checkable.
    [Fact]
    public void AddMarketDataKernel_RegistersProductionSnapshotReadBinding_ForDefaultRecords()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        using var provider = services.BuildServiceProvider();

        // Every default columnar record gets a binding registered alongside its store.
        var barBinding = provider.GetService<ISnapshotReadBinding<Bar>>();
        var scalarBinding = provider.GetService<ISnapshotReadBinding<ScalarObservation>>();
        var fxBinding = provider.GetService<ISnapshotReadBinding<FxRate>>();
        var factorBinding = provider.GetService<ISnapshotReadBinding<FactorObservation>>();
        var yieldBinding = provider.GetService<ISnapshotReadBinding<YieldCurveQuote>>();

        barBinding.Should().NotBeNull();
        scalarBinding.Should().NotBeNull();
        fxBinding.Should().NotBeNull();
        factorBinding.Should().NotBeNull();
        yieldBinding.Should().NotBeNull();

        // It is a PRODUCTION type in src/, not a test double — its assembly is the DI/Orchestration
        // package, and its name is the concrete LsmSnapshotReadBinding.
        barBinding!.GetType().Name.Should().StartWith("LsmSnapshotReadBinding");
        barBinding.GetType().Assembly.GetName().Name
            .Should().StartWith("Boutquin.MarketData.", "the binding is a shipped src/ type, never a test double");
    }
}
