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

using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.MarketData.Orchestration.Commands;

using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries.Columnar;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// AC-9 (§3.2): the optional one-shot <see cref="ImportLegacyL2SnapshotsCommand{TRequest, TRecord, TDatasetKey}"/>
/// warm-start importer. Run against N legacy L2 snapshots it must issue &#8805; N
/// <c>AppendAsync</c> calls; a subsequent <c>GetRangeAsync</c> over each imported range must return
/// records identical to the legacy snapshot; and a second run must leave the store unchanged
/// (idempotent, via LSM last-writer-wins on <c>(DatasetKey, Date)</c> — no importer-side dedup).
/// The read/append round-trip runs against the REAL <see cref="ServiceCollectionExtensions.AddMarketDataKernel"/>
/// composition root, so the importer is proven to populate the very store the pipeline reads.
/// </summary>
public sealed class ImportLegacyL2SnapshotsCommandTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-import-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a lingering segment handle must not fail an asserted test.
            }
        }
    }

    private ServiceProvider BuildKernel()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_tempDir, "snapshots"),
            rawDocumentDirectory: Path.Combine(_tempDir, "raw"),
            checkpointDirectory: Path.Combine(_tempDir, "checkpoints"),
            eventLogDirectory: Path.Combine(_tempDir, "events"),
            timeSeriesDirectory: Path.Combine(_tempDir, "timeseries"));
        return services.BuildServiceProvider();
    }

    private static PriceHistoryRequest PriceRequest(string ticker)
        => new([new Symbol(ticker)], new DateRange(new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31)));

    private static Bar Bar(DateOnly date, decimal close)
        => new(date, close, close, close, close, close, 1_000);

    // The default key the "default"-provider PriceBarDatasetKeyBuilder produces for a Daily request.
    private static PriceBarDatasetKey KeyFor(string ticker)
        => new("default", DataFrequency.Daily, ticker, null);

    // A site's snapshot source: yields the supplied (request, records) pairs.
    private sealed class ListSource(IReadOnlyList<LegacyL2Snapshot<PriceHistoryRequest, Bar>> items)
        : ILegacyL2SnapshotSource<PriceHistoryRequest, Bar>
    {
        public async IAsyncEnumerable<LegacyL2Snapshot<PriceHistoryRequest, Bar>> EnumerateAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }
    }

    // A spy store that counts AppendAsync calls (AC-9 clause (a)) while delegating reads/writes to a
    // real inner store so the round-trip still works. Only AppendAsync is instrumented.
    private sealed class AppendCountingStore<TDatasetKey, TRecord>(
        ITimeSeriesStore<TDatasetKey, TRecord> inner)
        : ITimeSeriesStore<TDatasetKey, TRecord>
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new()
    {
        public int AppendCallCount { get; private set; }

        public Task AppendAsync(TDatasetKey dataset, IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
        {
            AppendCallCount++;
            return inner.AppendAsync(dataset, records, cancellationToken);
        }

        public IAsyncEnumerable<TRecord> GetRangeAsync(TDatasetKey dataset, DateOnly from, DateOnly to, ColumnProjection? projection = null, CancellationToken cancellationToken = default)
            => inner.GetRangeAsync(dataset, from, to, projection, cancellationToken);

        public Task<DateRange?> GetCoverageAsync(TDatasetKey dataset, CancellationToken cancellationToken = default)
            => inner.GetCoverageAsync(dataset, cancellationToken);

        public Task<IReadOnlyList<DateRange>> GetGapsAsync(TDatasetKey dataset, DateRange requested, CancellationToken cancellationToken = default)
            => inner.GetGapsAsync(dataset, requested, cancellationToken);

        public IAsyncEnumerable<TRecord> GetTailAsync(TDatasetKey dataset, int count, DateOnly? onOrBefore = null, ColumnProjection? projection = null, CancellationToken cancellationToken = default)
            => inner.GetTailAsync(dataset, count, onOrBefore, projection, cancellationToken);

        public Task DeleteRangeAsync(TDatasetKey dataset, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
            => inner.DeleteRangeAsync(dataset, from, to, cancellationToken);
    }

    // A registry decorator that hands out the spy store for (PriceBarDatasetKey, Bar) and delegates
    // everything else to the inner registry.
    private sealed class SpyRegistry(
        ITimeSeriesStoreRegistry inner,
        AppendCountingStore<PriceBarDatasetKey, Bar> spy)
        : ITimeSeriesStoreRegistry
    {
        public ITimeSeriesStore<TDatasetKey, TRecord>? Resolve<TDatasetKey, TRecord>()
            where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
            where TRecord : ISerializable<TRecord>, IHasDate, new()
        {
            if (typeof(TDatasetKey) == typeof(PriceBarDatasetKey) && typeof(TRecord) == typeof(Bar))
            {
                return (ITimeSeriesStore<TDatasetKey, TRecord>)(object)spy;
            }

            return inner.Resolve<TDatasetKey, TRecord>();
        }

        public ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> ResolveEngine<TDatasetKey, TRecord>()
            where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
            where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
            => inner.ResolveEngine<TDatasetKey, TRecord>();

        public ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>? ResolveScanEngine<TDatasetKey, TRecord>()
            where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
            where TRecord : ISerializable<TRecord>, IHasDate, new()
            => inner.ResolveScanEngine<TDatasetKey, TRecord>();
    }

    private static ImportLegacyL2SnapshotsCommand<PriceHistoryRequest, Bar, PriceBarDatasetKey> Command(
        ServiceProvider sp,
        ITimeSeriesStoreRegistry registry,
        ILegacyL2SnapshotSource<PriceHistoryRequest, Bar> source)
        => new(
            registry,
            sp.GetRequiredService<IDatasetKeyBuilder<PriceHistoryRequest, PriceBarDatasetKey>>(),
            source);

    private static async Task<List<Bar>> ReadAllAsync(
        ITimeSeriesStore<PriceBarDatasetKey, Bar> store, PriceBarDatasetKey key)
    {
        var result = new List<Bar>();
        await foreach (var bar in store.GetRangeAsync(key, new DateOnly(2000, 1, 1), new DateOnly(2030, 12, 31)))
        {
            result.Add(bar);
        }

        return result;
    }

    [Fact]
    public async Task Ambiguous_price_batch_is_refused_before_append()
    {
        using var sp = BuildKernel();
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var spy = new AppendCountingStore<PriceBarDatasetKey, Bar>(registry.Resolve<PriceBarDatasetKey, Bar>()!);
        var request = PriceRequest("AAA") with { Symbols = [new Symbol("AAA"), new Symbol("BBB")] };
        var command = Command(sp, new SpyRegistry(registry, spy), new ListSource([
            new(request, [Bar(new DateOnly(2020, 1, 2), 10m), Bar(new DateOnly(2020, 1, 2), 20m)])]));

        await FluentActions.Invoking(() => command.ExecuteAsync()).Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);

        spy.AppendCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Ambiguous_dividend_batch_is_refused_before_append()
    {
        using var sp = BuildKernel();
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var request = new DividendHistoryRequest([new Symbol("AAA"), new Symbol("BBB")],
            new DateRange(new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31)));
        var command = new ImportLegacyL2SnapshotsCommand<DividendHistoryRequest, DividendPayment, DividendPaymentDatasetKey>(
            registry, sp.GetRequiredService<IDatasetKeyBuilder<DividendHistoryRequest, DividendPaymentDatasetKey>>(),
            new DividendSource(request));

        await FluentActions.Invoking(() => command.ExecuteAsync()).Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);

        var store = registry.Resolve<DividendPaymentDatasetKey, DividendPayment>()!;
        var key = sp.GetRequiredService<IDatasetKeyBuilder<DividendHistoryRequest, DividendPaymentDatasetKey>>()
            .Build(request with { Symbols = [new Symbol("AAA")] });
        var rows = new List<DividendPayment>();
        await foreach (var row in store.GetRangeAsync(key, request.Range.From, request.Range.To))
        {
            rows.Add(row);
        }
        rows.Should().BeEmpty();
    }

    private sealed class DividendSource(DividendHistoryRequest request) : ILegacyL2SnapshotSource<DividendHistoryRequest, DividendPayment>
    {
        public async IAsyncEnumerable<LegacyL2Snapshot<DividendHistoryRequest, DividendPayment>> EnumerateAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(request, [new DividendPayment(new DateOnly(2020, 1, 2), 1m, Currency.USD, DividendType.Unclassified), new DividendPayment(new DateOnly(2020, 1, 2), 2m, Currency.USD, DividendType.Unclassified)]);
            await Task.Yield();
        }
    }

    // AC-9 (a): N snapshots => >= N AppendAsync calls.
    [Fact]
    public async Task Run_against_N_snapshots_issues_at_least_N_appends()
    {
        using var sp = BuildKernel();
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var realStore = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var spy = new AppendCountingStore<PriceBarDatasetKey, Bar>(realStore);
        var spyRegistry = new SpyRegistry(registry, spy);

        var snapshots = new List<LegacyL2Snapshot<PriceHistoryRequest, Bar>>
        {
            new(PriceRequest("AAA"), [Bar(new DateOnly(2020, 1, 2), 10m), Bar(new DateOnly(2020, 1, 3), 11m)]),
            new(PriceRequest("BBB"), [Bar(new DateOnly(2020, 2, 2), 20m)]),
            new(PriceRequest("CCC"), [Bar(new DateOnly(2020, 3, 2), 30m), Bar(new DateOnly(2020, 3, 3), 31m)]),
        };

        var command = Command(sp, spyRegistry, new ListSource(snapshots));

        var result = await command.ExecuteAsync().ConfigureAwait(true);

        result.SnapshotsRead.Should().Be(3);
        spy.AppendCallCount.Should().BeGreaterThanOrEqualTo(3,
            "AC-9 requires >= N AppendAsync calls for N snapshots");
    }

    // AC-9 (b): after import, GetRangeAsync over each imported range returns records identical to the
    // legacy snapshot (value equality).
    [Fact]
    public async Task After_import_GetRange_returns_records_identical_to_the_legacy_snapshot()
    {
        using var sp = BuildKernel();
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;

        var aaaBars = new List<Bar> { Bar(new DateOnly(2020, 1, 2), 10m), Bar(new DateOnly(2020, 1, 3), 11m) };
        var bbbBars = new List<Bar> { Bar(new DateOnly(2020, 2, 2), 20m) };

        var snapshots = new List<LegacyL2Snapshot<PriceHistoryRequest, Bar>>
        {
            new(PriceRequest("AAA"), aaaBars),
            new(PriceRequest("BBB"), bbbBars),
        };

        var command = Command(sp, registry, new ListSource(snapshots));

        await command.ExecuteAsync().ConfigureAwait(true);

        var readAaa = await ReadAllAsync(store, KeyFor("AAA")).ConfigureAwait(true);
        var readBbb = await ReadAllAsync(store, KeyFor("BBB")).ConfigureAwait(true);

        readAaa.Should().BeEquivalentTo(aaaBars, "imported records round-trip identical to the snapshot");
        readBbb.Should().BeEquivalentTo(bbbBars);
    }

    // AC-9 (c): idempotent on second run — running the command twice leaves GetRangeAsync returning
    // the same set (LSM dedup on (DatasetKey, Date)), no doubling.
    [Fact]
    public async Task Second_run_is_idempotent_no_doubling()
    {
        using var sp = BuildKernel();
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;

        var aaaBars = new List<Bar> { Bar(new DateOnly(2020, 1, 2), 10m), Bar(new DateOnly(2020, 1, 3), 11m) };
        var snapshots = new List<LegacyL2Snapshot<PriceHistoryRequest, Bar>>
        {
            new(PriceRequest("AAA"), aaaBars),
        };

        // First run.
        await Command(sp, registry, new ListSource(snapshots)).ExecuteAsync().ConfigureAwait(true);
        var afterFirst = await ReadAllAsync(store, KeyFor("AAA")).ConfigureAwait(true);

        // Second run — identical (DatasetKey, Date) records re-appended.
        await Command(sp, registry, new ListSource(snapshots)).ExecuteAsync().ConfigureAwait(true);
        var afterSecond = await ReadAllAsync(store, KeyFor("AAA")).ConfigureAwait(true);

        afterFirst.Should().BeEquivalentTo(aaaBars);
        afterSecond.Should().BeEquivalentTo(aaaBars,
            "a second run dedups on (DatasetKey, Date) last-writer-wins — no doubling");
        afterSecond.Should().HaveCount(afterFirst.Count, "second run must not double the stored records");
    }

    // Explicit opt-in: an unmapped record type throws NotSupportedException naming it, rather than
    // silently dropping the snapshot.
    [Fact]
    public async Task Unmapped_record_type_throws_NotSupportedException_naming_it()
    {
        using var sp = BuildKernel();

        // An empty registry that resolves no store for any type.
        var emptyRegistry = new EmptyRegistry();
        var command = Command(
            sp,
            emptyRegistry,
            new ListSource([new(PriceRequest("AAA"), [Bar(new DateOnly(2020, 1, 2), 10m)])]));

        var act = () => command.ExecuteAsync();

        (await act.Should().ThrowAsync<NotSupportedException>().ConfigureAwait(true))
            .Which.Message.Should().Contain(nameof(Bar));
    }

    private sealed class EmptyRegistry : ITimeSeriesStoreRegistry
    {
        public ITimeSeriesStore<TDatasetKey, TRecord>? Resolve<TDatasetKey, TRecord>()
            where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
            where TRecord : ISerializable<TRecord>, IHasDate, new()
            => null;

        public ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> ResolveEngine<TDatasetKey, TRecord>()
            where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
            where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
            => throw new InvalidOperationException("no engine");

        public ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>? ResolveScanEngine<TDatasetKey, TRecord>()
            where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
            where TRecord : ISerializable<TRecord>, IHasDate, new()
            => null;
    }
}
