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
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.DependencyInjection;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

public sealed class Round3PipelineRegressionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory(nameof(Round3PipelineRegressionTests)).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ServiceProvider BuildHost(Action<IServiceCollection> configure, bool useTimeSeriesStore = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_root, "snapshots"),
            rawDocumentDirectory: Path.Combine(_root, "raw"),
            checkpointDirectory: Path.Combine(_root, "checkpoints"),
            eventLogDirectory: Path.Combine(_root, "events"),
            timeSeriesDirectory: Path.Combine(_root, "timeseries"),
            configurePipeline: options =>
            {
                options.UseTimeSeriesStore = useTimeSeriesStore;
                options.L1CacheTtl = TimeSpan.Zero;
            });
        configure(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Reselected_provider_keeps_complete_empty_window_after_restart()
    {
        var date = new DateOnly(2024, 6, 10);
        var primary = new EmptyDividendAdapter("A", 100);
        var fallback = new EmptyDividendAdapter("B", 0);
        DividendHistoryRequest Request(int offset) => new([new Symbol("VTI")], new(date.AddDays(offset), date.AddDays(offset)));
        void Configure(IServiceCollection services)
        {
            services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(primary);
            services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(fallback);
        }

        await using (var writer = BuildHost(Configure))
        {
            var pipeline = writer.GetRequiredService<IDataPipeline>();
            _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(Request(0)).ConfigureAwait(true);
            primary.Enabled = false;
            _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(Request(1)).ConfigureAwait(true);
            primary.Enabled = true;
            _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(Request(2)).ConfigureAwait(true);
            _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(Request(2)).ConfigureAwait(true);
            primary.Calls.Should().Be(2);
            fallback.Calls.Should().Be(1);
        }

        await using var reader = BuildHost(Configure);
        var replay = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(Request(2)).ConfigureAwait(true);
        primary.Calls.Should().Be(2, "the last selected provider must survive restart even when selected earlier");
        fallback.Calls.Should().Be(1);
        replay.Payload.Should().BeEmpty();
        replay.Coverage.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fan_out_missing_suffix_cannot_be_hidden_by_sibling_rows(bool throws)
    {
        var date = new DateOnly(2024, 6, 10);
        var adapter = new FactorAdapter(date.AddDays(2), throws);
        await using var host = BuildHost(services =>
            services.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(adapter));
        var pipeline = host.GetRequiredService<IDataPipeline>();
        var middle = new FactorSeriesRequest(new FactorDatasetId("R3"), new(date.AddDays(1), date.AddDays(1)));
        _ = await pipeline.ExecuteAsync<FactorSeriesRequest, FactorObservation>(middle).ConfigureAwait(true);
        var whole = middle with { Range = new(date, date.AddDays(2)) };
        var result = await pipeline.ExecuteAsync<FactorSeriesRequest, FactorObservation>(whole).ConfigureAwait(true);

        result.Payload.Should().HaveCount(throws ? 6 : 8);
        result.Coverage.Status.Should().Be(CompletenessStatus.Partial, "a known factor still lacks the requested suffix");
        result.Coverage.MissingPoints.Should().Be(1);
        result.Coverage.CoverageRatio.Should().Be(2m / 3m);
        result.Issues.Any(issue => issue.Code == IssueCode.PartitionFailed).Should().Be(throws);

        adapter.Repaired = true;
        var repaired = await pipeline.ExecuteAsync<FactorSeriesRequest, FactorObservation>(whole).ConfigureAwait(true);
        repaired.Payload.Should().HaveCount(9);
        repaired.Coverage.Status.Should().Be(CompletenessStatus.Complete);
        repaired.Coverage.MissingPoints.Should().Be(0);
    }

    [Fact]
    public async Task Row_replacement_serves_factor_pipeline_through_existing_scan_binding()
    {
        var date = new DateOnly(2024, 6, 10);
        var adapter = new FactorAdapter(date, throws: false) { Repaired = true };
        await using var host = BuildHost(services =>
        {
            services.ConfigureTimeSeriesStore<FactorObservationDatasetKey, FactorObservation>(
                options => options.WithSegmentRoot(Path.Combine(_root, "row-factors")).UseRowFormat());
            services.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(adapter);
        });
        DataEnvelope<IReadOnlyList<FactorObservation>>? result = null;
        Func<Task> read = async () => result = await host.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<FactorSeriesRequest, FactorObservation>(
                new(new FactorDatasetId("R3"), new(date, date))).ConfigureAwait(true);
        await read.Should().NotThrowAsync().ConfigureAwait(true);
        result!.Payload.Should().HaveCount(3);
        result.Payload.Select(row => row.FactorName).Should().BeEquivalentTo("A", "B", "C");
    }

    [Fact]
    public async Task Row_replacement_serves_bar_snapshot_through_existing_binding()
    {
        var date = new DateOnly(2024, 6, 10);
        await using var host = BuildHost(services =>
            services.ConfigureTimeSeriesStore<PriceBarDatasetKey, Bar>(
                options => options.WithSegmentRoot(Path.Combine(_root, "row-bars")).UseRowFormat()));
        var registry = host.GetRequiredService<ITimeSeriesStoreRegistry>();
        var key = new PriceBarDatasetKey("default", DataFrequency.Daily, "VTI", null);
        var expected = new Bar(date, 10m, 11m, 9m, 10m, 10m, 100);
        await registry.Resolve<PriceBarDatasetKey, Bar>()!.AppendAsync(key, [expected]).ConfigureAwait(true);
        await using var snapshot = await host.GetRequiredService<IDataPipeline>()
            .AcquireReadSnapshotAsync().ConfigureAwait(true);
        DataEnvelope<IReadOnlyList<Bar>>? result = null;
        Func<Task> read = async () => result = await snapshot.ExecuteAsync<Bar>(
            new PriceHistoryRequest([new Symbol("VTI")], new(date, date))).ConfigureAwait(true);
        await read.Should().NotThrowAsync().ConfigureAwait(true);
        result!.Payload.Should().Equal(expected);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task Default_price_dispatch_is_single_identity_in_both_modes(bool useTimeSeriesStore, int count)
    {
        var adapter = new BatchPriceAdapter();
        await using var host = BuildHost(services =>
            services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter), useTimeSeriesStore);
        var date = new DateOnly(2024, 6, 10);
        var symbols = new[] { new Symbol("A"), new Symbol("B") }.Take(count).ToArray();
        var result = await host.GetRequiredService<IDataPipeline>().ExecuteAsync<PriceHistoryRequest, Bar>(
            new(symbols, new(date, date))).ConfigureAwait(true);
        result.Payload.Select(row => row.Close).Should().Equal(symbols.Select(symbol => (decimal)symbol.Ticker[0]));
        adapter.Shapes.Should().Equal(Enumerable.Repeat(1, count));
        result.Issues.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task Default_fx_dispatch_is_single_identity_in_both_modes(bool useTimeSeriesStore, int count)
    {
        var adapter = new BatchFxAdapter();
        await using var host = BuildHost(services =>
            services.AddSingleton<IDataSourceAdapter<FxHistoryRequest, FxRate>>(adapter), useTimeSeriesStore);
        var date = new DateOnly(2024, 6, 10);
        var pairs = new[] { new FxPair(Currency.EUR, Currency.USD), new FxPair(Currency.USD, Currency.CAD) }.Take(count).ToArray();
        var result = await host.GetRequiredService<IDataPipeline>().ExecuteAsync<FxHistoryRequest, FxRate>(
            new(pairs, new(date, date))).ConfigureAwait(true);
        result.Payload.Select(row => row.Rate).Should().Equal(pairs.Select(pair => pair == pairs[0] ? 1m : 2m));
        adapter.Shapes.Should().Equal(Enumerable.Repeat(1, count));
        result.Issues.Should().BeEmpty();
    }

    private sealed class BatchPriceAdapter : IDataSourceAdapter<PriceHistoryRequest, Bar>
    {
        public ProviderCode ProviderKey { get; } = new("r3-price");
        public List<int> Shapes { get; } = [];
        public bool CanHandle(PriceHistoryRequest request) => true;
        public Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(PriceHistoryRequest request, CancellationToken cancellationToken = default)
        {
            Shapes.Add(request.Symbols.Count);
            IReadOnlyList<Bar> rows = request.Symbols.Select(symbol =>
                new Bar(request.Range.From, symbol.Ticker[0], symbol.Ticker[0], symbol.Ticker[0], symbol.Ticker[0], symbol.Ticker[0], 1)).ToArray();
            return Task.FromResult(new DataEnvelope<IReadOnlyList<Bar>>(rows, new(rows.Count, rows.Count, 0, 1m), [], []));
        }
    }

    private sealed class BatchFxAdapter : IDataSourceAdapter<FxHistoryRequest, FxRate>
    {
        public ProviderCode ProviderKey { get; } = new("r3-fx");
        public List<int> Shapes { get; } = [];
        public bool CanHandle(FxHistoryRequest request) => true;
        public Task<DataEnvelope<IReadOnlyList<FxRate>>> FetchAsync(FxHistoryRequest request, CancellationToken cancellationToken = default)
        {
            Shapes.Add(request.Pairs.Count);
            IReadOnlyList<FxRate> rows = request.Pairs.Select(pair =>
                new FxRate(request.Range.From, pair == new FxPair(Currency.EUR, Currency.USD) ? 1m : 2m)).ToArray();
            return Task.FromResult(new DataEnvelope<IReadOnlyList<FxRate>>(rows, new(rows.Count, rows.Count, 0, 1m), [], []));
        }
    }

    private sealed class FactorAdapter(DateOnly suffix, bool throws)
        : IDataSourceAdapter<FactorSeriesRequest, FactorObservation>
    {
        public ProviderCode ProviderKey { get; } = new("r3-factors");
        public bool Repaired { get; set; }
        public bool CanHandle(FactorSeriesRequest request) => true;
        public Task<DataEnvelope<IReadOnlyList<FactorObservation>>> FetchAsync(
            FactorSeriesRequest request, CancellationToken cancellationToken = default)
        {
            var incomplete = request.Range.From == suffix && !Repaired;
            if (incomplete && throws)
            {
                throw new IOException("suffix unavailable");
            }

            IReadOnlyList<FactorObservation> rows = incomplete
                ? [new(request.Range.From, "A", 1m), new(request.Range.From, "B", 2m)]
                : [new(request.Range.From, "A", 1m), new(request.Range.From, "B", 2m), new(request.Range.From, "C", 3m)];
            return Task.FromResult(new DataEnvelope<IReadOnlyList<FactorObservation>>(
                rows, new(3, rows.Count, 3 - rows.Count, rows.Count / 3m), [], []));
        }
    }

    private sealed class EmptyDividendAdapter(string name, int priority)
        : IDataSourceAdapter<DividendHistoryRequest, DividendPayment>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new(name);
        public int Priority { get; } = priority;
        public bool Enabled { get; set; } = true;
        public int Calls { get; private set; }
        public bool CanHandle(DividendHistoryRequest request) => Enabled;
        public Task<DataEnvelope<IReadOnlyList<DividendPayment>>> FetchAsync(
            DividendHistoryRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new DataEnvelope<IReadOnlyList<DividendPayment>>([], new(0, 0, 0, 1m), [], []));
        }
    }
}
