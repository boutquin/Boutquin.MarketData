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
using Boutquin.MarketData.Abstractions.Exceptions;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.Keys;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.Cache.WriteThrough;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.MarketData.Orchestration.Commands;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

internal sealed class PriceBasisKernel : IAsyncDisposable
{
    internal static PriceBasis Tiingo { get; } = new(PriceAdjustment.All, AdjustedCloseSource.ProviderSupplied, PriceAdjustment.None);
    internal static PriceBasis Twelve { get; } = new(PriceAdjustment.All, AdjustedCloseSource.AdapterComputed, PriceAdjustment.Splits);
    internal static DateRange Range { get; } = new(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 16));
    internal static PriceHistoryRequest Request(PriceBasis basis, bool batch = false) =>
        new(batch ? [new Symbol("SPY"), new Symbol("QQQ")] : [new Symbol("SPY")], Range) { Basis = basis };
    internal static Bar Row(DateOnly date, decimal close = 100m) => new(date, close, close, close, close, close, 1000);
    internal static IReadOnlyList<Bar> Rows(DateRange range, decimal close = 100m) =>
        Enumerable.Range(0, range.To.DayNumber - range.From.DayNumber + 1).Select(range.From.AddDays)
            .Where(date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Select(date => Row(date, close)).ToArray();
    internal static PriceBarDatasetKey Key(string provider = "default/none.provider.none") =>
        new(provider, DataFrequency.Daily, "SPY", null);
    internal sealed class Adapter(string provider, PriceBasis declared, int delivery = 0, int priority = 1) :
        IDataSourceAdapter<PriceHistoryRequest, Bar>, IPriceBasisAdapter, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey => new(provider);
        public PriceBasis DeliveredBasis => declared;
        public int Priority => priority;
        internal List<PriceHistoryRequest> Requests { get; } = [];
        public bool CanHandle(PriceHistoryRequest request) => request.Basis == DeliveredBasis;
        public Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(PriceHistoryRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (delivery == 3) { throw new HttpRequestException("fixture unavailable"); }
            PriceBasis? stamp = delivery == 2 ? null : delivery == 1 ? Twelve : delivery == 4 ? Tiingo : declared;
            var rows = Rows(request.Range);
            return Task.FromResult(new DataEnvelope<IReadOnlyList<Bar>>(rows, new(rows.Count, rows.Count, 0, 1m), [],
                [new(new ProviderCode(provider), request.DatasetKey, LicenseType.Free, RetrievalMode.Live, FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, null) { PriceBasis = stamp }]));
        }
    }
    internal ServiceProvider Services { get; }
    internal IDataPipeline Pipeline => Services.GetRequiredService<IDataPipeline>();
    internal ITimeSeriesStore<PriceBarDatasetKey, Bar> Store =>
        Services.GetRequiredService<ITimeSeriesStoreRegistry>().Resolve<PriceBarDatasetKey, Bar>()!;
    internal WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<Bar>>> L2 =>
        Services.GetRequiredService<WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<Bar>>>>();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pb-" + Guid.NewGuid().ToString("N"));
    internal PriceBasisKernel(IReadOnlyList<Adapter> adapters, bool timeSeries = false, bool incremental = false,
        bool continuation = true, string? customProvider = null, IRequestPartitioner<PriceHistoryRequest>? partitioner = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        if (customProvider is not null)
        {
            services.AddSingleton<IDatasetKeyBuilder<PriceHistoryRequest, PriceBarDatasetKey>>(new PriceBarDatasetKeyBuilder(customProvider));
        }
        services.AddMarketDataKernel(snapshotDirectory: Path.Combine(_root, "snapshots"), rawDocumentDirectory: Path.Combine(_root, "raw"),
            checkpointDirectory: Path.Combine(_root, "checkpoints"), eventLogDirectory: Path.Combine(_root, "events"),
            timeSeriesDirectory: Path.Combine(_root, "series"),
            configurePipeline: options =>
            {
                options.UseTimeSeriesStore = timeSeries;
                options.AllowIncrementalPriceHistoryGapFill = incremental;
                options.ContinueOnPartitionFailure = continuation;
            });
        foreach (var adapter in adapters) { services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter); }
        if (partitioner is not null) { services.AddSingleton(partitioner); }
        Services = services.BuildServiceProvider();
    }
    internal async Task Seed(string provider, DateRange? range = null) =>
        await Store.AppendAsync(Key(provider), Rows(range ?? Range, 9876m));
    internal Task<DataEnvelope<IReadOnlyList<Bar>>> Read(PriceBasis basis, bool batch = false) =>
        Pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(Request(basis, batch));
    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }
    internal sealed class Source(PriceHistoryRequest request, bool attest = true) : ILegacyL2SnapshotSource<PriceHistoryRequest, Bar>
    {
        public async IAsyncEnumerable<LegacyL2Snapshot<PriceHistoryRequest, Bar>> EnumerateAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(request, Rows(request.Range)) { AttestsRawPriceBasis = attest };
            await Task.CompletedTask;
        }
    }
    internal async Task Import(PriceHistoryRequest request, bool attest = true)
    {
        var command = new ImportLegacyL2SnapshotsCommand<PriceHistoryRequest, Bar, PriceBarDatasetKey>(
            Services.GetRequiredService<ITimeSeriesStoreRegistry>(),
            Services.GetRequiredService<IDatasetKeyBuilder<PriceHistoryRequest, PriceBarDatasetKey>>(), new Source(request, attest));
        await command.ExecuteAsync();
    }
}

public sealed class PriceBasisTimeSeriesMergeTests
{
    [Fact]
    public async Task Partition_merge_checks_the_original_requested_basis()
    {
        await using var h = new PriceBasisKernel(
            [new("raw", PriceBasis.Raw), new("twelve", PriceBasisKernel.Twelve)], true, true,
            partitioner: new BasisChangingPartitioner());
        var act = () => h.Read(PriceBasisKernel.Twelve, batch: true);
        var exception = (await act.Should().ThrowAsync<PriceBasisMismatchException>()).Which;
        exception.Requested.Should().Be(PriceBasisKernel.Twelve);
        exception.Stored.Should().Be(PriceBasis.Raw);
    }

    private sealed class BasisChangingPartitioner : IRequestPartitioner<PriceHistoryRequest>
    {
        public IReadOnlyList<PriceHistoryRequest> Partition(PriceHistoryRequest request) =>
            request.Symbols.Select(symbol => request with { Symbols = [symbol], Basis = PriceBasis.Raw }).ToArray();
    }
}

public sealed class PriceBasisPartitionBasisTests
{
    // A partitioner that rewrites Basis must be refused for a single partition too, before any
    // adapter runs, on both execution paths: otherwise raw bars would be cached or returned
    // under an adjusted request key.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Single_partition_with_a_different_basis_is_refused_before_dispatch(bool timeSeries, bool batch)
    {
        var raw = new PriceBasisKernel.Adapter("raw", PriceBasis.Raw);
        var twelve = new PriceBasisKernel.Adapter("twelve", PriceBasisKernel.Twelve);
        await using var h = new PriceBasisKernel([raw, twelve], timeSeries, incremental: true,
            partitioner: new RawBasisPartitioner());
        var act = () => h.Read(PriceBasisKernel.Twelve, batch);
        var exception = (await act.Should().ThrowAsync<PriceBasisMismatchException>()).Which;
        exception.Requested.Should().Be(PriceBasisKernel.Twelve);
        exception.Stored.Should().Be(PriceBasis.Raw);
        raw.Requests.Should().BeEmpty();
        twelve.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Basis_preserving_single_partition_is_unaffected()
    {
        var twelve = new PriceBasisKernel.Adapter("twelve", PriceBasisKernel.Twelve);
        await using var h = new PriceBasisKernel([twelve], timeSeries: false, incremental: true,
            partitioner: new IdentityPartitioner());
        var result = await h.Read(PriceBasisKernel.Twelve);
        result.Provenance.Should().OnlyContain(p => p.PriceBasis == PriceBasisKernel.Twelve);
    }

    private sealed class RawBasisPartitioner : IRequestPartitioner<PriceHistoryRequest>
    {
        public IReadOnlyList<PriceHistoryRequest> Partition(PriceHistoryRequest request) =>
            [request with { Basis = PriceBasis.Raw }];
    }

    private sealed class IdentityPartitioner : IRequestPartitioner<PriceHistoryRequest>
    {
        public IReadOnlyList<PriceHistoryRequest> Partition(PriceHistoryRequest request) => [request];
    }
}

public sealed class PriceBasisPartitionWindowTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Adjusted_date_partitions_are_refused_before_adapter_dispatch(bool timeSeries, bool twelve, bool single)
    {
        var basis = twelve ? PriceBasisKernel.Twelve : PriceBasisKernel.Tiingo;
        var adapter = new PriceBasisKernel.Adapter("fixture", basis);
        await using var host = new PriceBasisKernel([adapter], timeSeries, incremental: true,
            partitioner: new DatePartitioner(single));
        Func<Task> read = () => host.Read(basis);
        await read.Should().ThrowExactlyAsync<InvalidOperationException>()
            .WithMessage("Adjusted price partitions must preserve the original request window; independently anchored levels cannot be concatenated.");
        adapter.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Raw_date_partitions_remain_supported(bool timeSeries)
    {
        var adapter = new PriceBasisKernel.Adapter("fixture", PriceBasis.Raw);
        await using var host = new PriceBasisKernel([adapter], timeSeries, incremental: true,
            partitioner: new DatePartitioner(false));
        var result = await host.Read(PriceBasis.Raw);
        result.Payload.Should().NotBeEmpty();
        adapter.Requests.Should().HaveCount(2);
        result.Provenance.Should().OnlyContain(p => p.PriceBasis == PriceBasis.Raw);
    }

    private sealed class DatePartitioner(bool single) : IRequestPartitioner<PriceHistoryRequest>
    {
        public IReadOnlyList<PriceHistoryRequest> Partition(PriceHistoryRequest request)
        {
            var first = (PriceHistoryRequest)request.WithRange(new DateRange(request.Range.From, request.Range.From.AddDays(4)));
            return single ? [first] : [first,
                (PriceHistoryRequest)request.WithRange(new DateRange(request.Range.From.AddDays(5), request.Range.To))];
        }
    }
}

public sealed class PriceHistoryGapFillBasisTests
{
    [Fact] public Task Option_off_raw_declines() => Check(false, PriceBasis.Raw, false);
    [Fact] public Task Option_on_tiingo_declines() => Check(true, PriceBasisKernel.Tiingo, false);
    [Fact] public Task Option_on_twelvedata_declines() => Check(true, PriceBasisKernel.Twelve, false);
    [Fact] public Task Option_on_raw_fills_only_gap() => Check(true, PriceBasis.Raw, true);
    private static async Task Check(bool option, PriceBasis basis, bool usesStore)
    {
        var adapter = new PriceBasisKernel.Adapter("fixture", basis);
        await using var h = new PriceBasisKernel([adapter], timeSeries: true, incremental: option);
        await h.Seed("default/" + basis.Token, new(PriceBasisKernel.Range.From, PriceBasisKernel.Range.From.AddDays(4)));
        var result = await h.Read(basis);
        adapter.Requests.Should().ContainSingle().Which.Range.Should().Be(
            usesStore ? new DateRange(PriceBasisKernel.Range.From.AddDays(5), PriceBasisKernel.Range.To) : PriceBasisKernel.Range);
        result.Payload.Any(row => row.Close == 9876m).Should().Be(usesStore);
        result.Provenance.Should().OnlyContain(p => p.PriceBasis == basis);
    }
    [Fact]
    public async Task Importer_refuses_unattested_raw_bars_before_append()
    {
        await using var h = new PriceBasisKernel([], timeSeries: true, incremental: true);
        var act = () => h.Import(PriceBasisKernel.Request(PriceBasis.Raw), attest: false);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*AttestsRawPriceBasis*");
        (await h.Store.GetRangeAsync(PriceBasisKernel.Key("default"), PriceBasisKernel.Range.From, PriceBasisKernel.Range.To).ToListAsync()).Should().BeEmpty();
    }
    [Fact]
    public async Task Importer_accepts_attested_raw_bars()
    {
        await using var h = new PriceBasisKernel([], timeSeries: true, incremental: true);
        await h.Import(PriceBasisKernel.Request(PriceBasis.Raw), attest: true);
        (await h.Store.GetRangeAsync(PriceBasisKernel.Key("default/none.provider.none"), PriceBasisKernel.Range.From, PriceBasisKernel.Range.To).ToListAsync()).Should().NotBeEmpty();
    }
    [Fact]
    public async Task Importer_refuses_nonraw_before_append()
    {
        await using var h = new PriceBasisKernel([], timeSeries: true, incremental: true);
        var act = () => h.Import(PriceBasisKernel.Request(PriceBasisKernel.Tiingo));
        await act.Should().ThrowAsync<PriceBasisNotSupportedException>();
        (await h.Store.GetRangeAsync(PriceBasisKernel.Key("default/" + PriceBasisKernel.Tiingo.Token), PriceBasisKernel.Range.From, PriceBasisKernel.Range.To).ToListAsync()).Should().BeEmpty();
        (await h.Store.GetRangeAsync(PriceBasisKernel.Key("default"), PriceBasisKernel.Range.From, PriceBasisKernel.Range.To).ToListAsync()).Should().BeEmpty();
    }
    [Fact]
    public async Task Snapshot_refuses_nonraw_stored_range()
    {
        await using var h = new PriceBasisKernel([], timeSeries: true, incremental: true);
        await h.Seed("default/" + PriceBasisKernel.Tiingo.Token);
        await using var snapshot = await h.Pipeline.AcquireReadSnapshotAsync();
        var act = () => snapshot.ExecuteAsync<Bar>(PriceBasisKernel.Request(PriceBasisKernel.Tiingo));
        var exception = (await act.Should().ThrowAsync<PriceBasisMismatchException>()).Which;
        exception.Stored.Should().Be(PriceBasis.Raw);
    }
}

public sealed class LegacySnapshotNotServedTests
{
    [Fact]
    public async Task LegacySnapshotNotServed_and_original_entry_is_unchanged()
    {
        var adapter = new PriceBasisKernel.Adapter("fixture", PriceBasisKernel.Tiingo);
        await using var h = new PriceBasisKernel([adapter]);
        var key = CacheKeyBuilder.FromDateRange("pipeline-v2/Bar/Daily", "SPY", PriceBasisKernel.Range.From, PriceBasisKernel.Range.To);
        await h.L2.WriteAsync(key, new(new SchemaVersion(1), new(DateTimeOffset.UtcNow, [], []), PriceBasisKernel.Rows(PriceBasisKernel.Range, 9876m)));
        (await h.Read(PriceBasisKernel.Tiingo)).Payload.Should().OnlyContain(row => row.Close != 9876m);
        adapter.Requests.Should().ContainSingle();
        var preserved = (await h.L2.TryReadAsync(key))!;
        preserved.Payload.Should().OnlyContain(row => row.Close == 9876m);
        preserved.Metadata.Provenance.Should().BeEmpty();
    }
}

public sealed class PriceBasisMixingMatrixTests
{
    [Fact]
    public async Task PriceBasisMixingMatrix_matches_all_96_cells()
    {
        var errors = new List<string>();
        var histogram = new Dictionary<string, int>();
        var count = 0;
        var mixed = 0;
        foreach (var requested in new[] { PriceBasisKernel.Tiingo, PriceBasisKernel.Twelve })
        {
            for (var p = 0; p < 4; p++)
            {
                for (var f = 0; f < 4; f++)
                {
                    for (var cache = 0; cache < 3; cache++)
                    {
                        // 0=Tiingo stamp, 1=Twelve stamp, 2=unstamped, 3=HTTP failure.
                        var primary = new PriceBasisKernel.Adapter("primary", PriceBasisKernel.Tiingo, p == 0 ? 4 : p, 2);
                        var fallback = new PriceBasisKernel.Adapter("fallback", PriceBasisKernel.Twelve, f == 0 ? 4 : f, 1);
                        await using var h = new PriceBasisKernel([primary, fallback]);
                        if (cache != 0)
                        {
                            var dataset = cache == 1 ? "SPY" : requested == PriceBasisKernel.Tiingo ? "SPY/basis:all.provider.none" : "SPY/basis:all.computed.splits";
                            var other = requested == PriceBasisKernel.Tiingo ? PriceBasisKernel.Twelve : PriceBasisKernel.Tiingo;
                            IReadOnlyList<DataProvenance> provenance = cache == 1 ? [] :
                                [new(new ProviderCode("forged"), dataset, LicenseType.Free, RetrievalMode.Live, FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, null) { PriceBasis = other }];
                            var key = CacheKeyBuilder.FromDateRange("pipeline-v2/Bar/Daily", dataset, PriceBasisKernel.Range.From, PriceBasisKernel.Range.To);
                            await h.L2.WriteAsync(key, new(new SchemaVersion(1), new(DateTimeOffset.UtcNow, provenance, []), PriceBasisKernel.Rows(PriceBasisKernel.Range, 9876m)));
                        }
                        var expected = cache == 2 ? "Mismatch" : requested == PriceBasisKernel.Tiingo
                            ? p == 0 ? "Live(Tiingo)" : "AllRejected"
                            : f == 1 ? "Live(TwelveData)" : "AllRejected";
                        histogram[expected] = histogram.GetValueOrDefault(expected) + 1;
                        string actual;
                        try
                        {
                            var result = await h.Read(requested);
                            if (result.Payload.Count == 0 && result.Issues.Count(issue => issue.Code.Value == "PARTITION_FAILED") == 1) { actual = "AllRejected"; }
                            else if (result.Provenance.Any(entry => entry.RetrievalMode == RetrievalMode.Snapshot)) { actual = "Snapshot"; }
                            else if (result.Payload.Count > 0 && result.Provenance.Count > 0 && result.Provenance.All(entry => entry.RetrievalMode == RetrievalMode.Live && entry.PriceBasis == requested))
                            { actual = requested == PriceBasisKernel.Tiingo ? "Live(Tiingo)" : "Live(TwelveData)"; }
                            else { actual = "UnprovenOrMixed"; mixed++; }
                        }
                        catch (PriceBasisMismatchException) { actual = "Mismatch"; }
                        catch (PriceBasisNotSupportedException) { actual = "NotSupported"; }
                        catch (Exception ex) { actual = "Other(" + ex.GetType().Name + ")"; }
                        count++;
                        var currentKey = CacheKeyBuilder.FromDateRange("pipeline-v2/Bar/Daily",
                            requested == PriceBasisKernel.Tiingo ? "SPY/basis:all.provider.none" : "SPY/basis:all.computed.splits",
                            PriceBasisKernel.Range.From, PriceBasisKernel.Range.To);
                        if (actual is "AllRejected" or "Mismatch")
                        {
                            var l1 = await h.Services.GetRequiredService<ILocalCache>().GetAsync<DataEnvelope<IReadOnlyList<Bar>>>(currentKey);
                            var l2 = await h.L2.TryReadAsync(currentKey);
                            if (l1 is not null || (actual == "AllRejected" && l2 is not null))
                            {
                                errors.Add($"Failed cell {count} wrote a new authoritative cache entry");
                            }
                            if (actual == "Mismatch" && (l2 is null || l2.Payload.Any(row => row.Close != 9876m)
                                || l2.Metadata.Provenance.Any(entry => entry.PriceBasis == requested)))
                            {
                                errors.Add($"Mismatch cell {count} changed the forged entry");
                            }
                        }
                        if (actual != expected) { errors.Add($"{requested.Token}/{p}/{f}/{cache}: expected {expected}, actual {actual}"); }
                    }
                }
            }
        }
        var output = Environment.GetEnvironmentVariable("PRICE_BASIS_MATRIX_OUT");
        if (output is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { cells_planned = 96, cells_executed = count, cells_unexpected = errors.Count, mixed_or_mismatched_returned = mixed }));
        }
        count.Should().Be(96);
        histogram.Should().BeEquivalentTo(new Dictionary<string, int> { ["Mismatch"] = 32, ["AllRejected"] = 48, ["Live(Tiingo)"] = 8, ["Live(TwelveData)"] = 8 });
        errors.Should().BeEmpty();
        mixed.Should().Be(0);
    }
}

public sealed class PriceBasisEscapesTimeSeriesPartitionCatchTests
{
    [Fact] public Task Refusal_escapes_continue_true() => Refusal(true);
    [Fact] public Task Refusal_escapes_continue_false() => Refusal(false);
    private static async Task Refusal(bool continuation)
    {
        await using var h = new PriceBasisKernel([new("fixture", PriceBasisKernel.Tiingo)], true, true, continuation);
        var act = () => h.Read(PriceBasis.Raw, batch: true);
        await act.Should().ThrowAsync<PriceBasisNotSupportedException>();
    }
    [Fact]
    public async Task Http_failure_stays_partition_failed()
    {
        await using var h = new PriceBasisKernel([new("fixture", PriceBasis.Raw, delivery: 3)], true, true);
        var result = await h.Read(PriceBasis.Raw, batch: true);
        result.Payload.Should().BeEmpty();
        result.Issues.Should().Contain(issue => issue.Code.Value == "PARTITION_FAILED");
    }
}

public sealed class PriceBasisSeriesIsolationTests
{
    [Fact] public Task Executor_ignores_legacy_default_series() => Executor(null);
    [Fact] public Task Executor_isolates_consumer_registered_default_builder() => Executor("default");
    private static async Task Executor(string? custom)
    {
        var adapter = new PriceBasisKernel.Adapter("fixture", PriceBasis.Raw);
        await using var h = new PriceBasisKernel([adapter], true, true, customProvider: custom);
        await h.Seed("default");
        (await h.Read(PriceBasis.Raw)).Payload.Should().OnlyContain(row => row.Close != 9876m);
        adapter.Requests.Should().ContainSingle().Which.Range.Should().Be(PriceBasisKernel.Range);
    }
    [Fact]
    public async Task Executor_suffixes_custom_builder_provider()
    {
        await using var h = new PriceBasisKernel([new("fixture", PriceBasis.Raw)], true, true, customProvider: "custom");
        await h.Read(PriceBasis.Raw);
        (await h.Store.GetRangeAsync(PriceBasisKernel.Key("custom/none.provider.none"), PriceBasisKernel.Range.From, PriceBasisKernel.Range.To).ToListAsync()).Should().NotBeEmpty();
        (await h.Store.GetRangeAsync(PriceBasisKernel.Key("custom"), PriceBasisKernel.Range.From, PriceBasisKernel.Range.To).ToListAsync()).Should().BeEmpty();
    }
    [Fact]
    public async Task SnapshotRead_ignores_legacy_default_series()
    {
        await using var h = new PriceBasisKernel([new("fixture", PriceBasis.Raw)], true, true);
        await h.Seed("default");
        await h.Read(PriceBasis.Raw);
        await using var snapshot = await h.Pipeline.AcquireReadSnapshotAsync();
        var result = await snapshot.ExecuteAsync<Bar>(PriceBasisKernel.Request(PriceBasis.Raw));
        result.Payload.Should().NotBeEmpty().And.OnlyContain(row => row.Close != 9876m);
        result.Provenance.Should().OnlyContain(p => p.PriceBasis == PriceBasis.Raw);
    }
    [Fact]
    public async Task Importer_appends_under_suffixed_provider()
    {
        await using var h = new PriceBasisKernel([], true, true);
        await h.Seed("default");
        await h.Import(PriceBasisKernel.Request(PriceBasis.Raw));
        (await h.Store.GetRangeAsync(PriceBasisKernel.Key(), PriceBasisKernel.Range.From, PriceBasisKernel.Range.To).ToListAsync()).Should().NotBeEmpty().And.OnlyContain(row => row.Close != 9876m);
        (await h.Store.GetRangeAsync(PriceBasisKernel.Key("default"), PriceBasisKernel.Range.From, PriceBasisKernel.Range.To).ToListAsync()).Should().OnlyContain(row => row.Close == 9876m);
    }
}

public sealed class PriceBasisRefusedOnTimeSeriesPathTests
{
    [Fact] public Task Empty_store_refuses_undeclared_raw() => Refusal(false);
    [Fact] public Task Fully_stored_range_refuses_undeclared_raw() => Refusal(true);
    private static async Task Refusal(bool seed)
    {
        var tiingo = new PriceBasisKernel.Adapter("tiingo", PriceBasisKernel.Tiingo);
        var twelve = new PriceBasisKernel.Adapter("twelve", PriceBasisKernel.Twelve);
        await using var h = new PriceBasisKernel([tiingo, twelve], true, true);
        if (seed) { await h.Seed("default/none.provider.none"); }
        var act = () => h.Read(PriceBasis.Raw);
        await act.Should().ThrowAsync<PriceBasisNotSupportedException>();
        tiingo.Requests.Should().BeEmpty();
        twelve.Requests.Should().BeEmpty();
        if (!seed) { (await h.Store.GetRangeAsync(PriceBasisKernel.Key(), PriceBasisKernel.Range.From, PriceBasisKernel.Range.To).ToListAsync()).Should().BeEmpty(); }
    }
    [Fact]
    public async Task Declared_raw_preserves_basis_on_gap_request()
    {
        var tiingo = new PriceBasisKernel.Adapter("tiingo", PriceBasisKernel.Tiingo);
        var twelve = new PriceBasisKernel.Adapter("twelve", PriceBasisKernel.Twelve);
        var raw = new PriceBasisKernel.Adapter("raw", PriceBasis.Raw);
        await using var h = new PriceBasisKernel([tiingo, twelve, raw], true, true);
        (await h.Read(PriceBasis.Raw)).Payload.Should().NotBeEmpty();
        raw.Requests.Should().ContainSingle().Which.Basis.Should().Be(PriceBasis.Raw);
        tiingo.Requests.Should().BeEmpty();
        twelve.Requests.Should().BeEmpty();
    }
}
