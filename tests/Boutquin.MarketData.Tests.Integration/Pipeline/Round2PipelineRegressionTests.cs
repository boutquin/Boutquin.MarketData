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
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.DependencyInjection;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

public sealed class Round2PipelineRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"marketdata-r2-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consumer_preserved_price_batch_cannot_contaminate_singleton_series(bool useTimeSeriesStore)
    {
        var adapter = new PriceAdapter();
        var partitioner = new WholePricePartitioner();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_directory, "snapshots"),
            rawDocumentDirectory: Path.Combine(_directory, "raw"),
            checkpointDirectory: Path.Combine(_directory, "checkpoints"),
            eventLogDirectory: Path.Combine(_directory, "events"),
            timeSeriesDirectory: Path.Combine(_directory, "timeseries"),
            configurePipeline: options =>
            {
                options.UseTimeSeriesStore = useTimeSeriesStore;
                options.L1CacheTtl = TimeSpan.Zero;
            });
        services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter);
        services.AddSingleton<IRequestPartitioner<PriceHistoryRequest>>(partitioner);
        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IDataPipeline>();
        var range = new DateRange(new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2));
        var batch = new PriceHistoryRequest([new Symbol("AAPL"), new Symbol("MSFT")], range);

        var aggregate = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(batch).ConfigureAwait(true);
        adapter.Requests.Should().ContainSingle().Which.Symbols.Should().Equal(batch.Symbols);
        partitioner.Calls.Should().Be(1);

        var singleton = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            batch with { Symbols = [new Symbol("AAPL")] }).ConfigureAwait(true);
        singleton.Payload.Should().ContainSingle().Which.Open.Should().Be(100m,
            "a symbol-less aggregate bar must never be stored under the first symbol's key");
        aggregate.Payload.Select(bar => bar.Open).Should().Equal(100m, 101m);
        adapter.Requests.Should().HaveCount(2, "the aggregate cannot establish singleton store coverage");
        adapter.Requests[1].Symbols.Should().ContainSingle().Which.Ticker.Should().Be("AAPL");

        var repeated = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            batch with { Symbols = [new Symbol("AAPL")] }).ConfigureAwait(true);
        repeated.Payload.Should().ContainSingle().Which.Open.Should().Be(100m);
        adapter.Requests.Should().HaveCount(2, "the unambiguous singleton remains cacheable");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capability_exception_reaches_healthy_backup_in_both_pipeline_modes(bool useTimeSeriesStore)
    {
        var backup = new PriceAdapter();
        using var host = MetadataHost(backup, useTimeSeriesStore, new CapabilityFailureAdapter(false));
        var request = new PriceHistoryRequest([new Symbol("AAPL")],
            new(new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2)));
        var execute = async () => await host.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<PriceHistoryRequest, Bar>(request).ConfigureAwait(true);
        var result = await execute.Should().NotThrowAsync("capability failures cannot bypass fallback");
        result.Subject.Payload.Should().ContainSingle().Which.Open.Should().Be(100m);
        backup.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capability_cancellation_is_not_replaced_by_backup_data(bool useTimeSeriesStore)
    {
        var backup = new PriceAdapter();
        using var host = MetadataHost(backup, useTimeSeriesStore, new CapabilityFailureAdapter(true));
        var execute = async () => await host.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<PriceHistoryRequest, Bar>(new([new Symbol("AAPL")],
                new(new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2)))).ConfigureAwait(true);
        await execute.Should().ThrowAsync<OperationCanceledException>();
        backup.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Time_series_cache_and_restart_clear_only_the_live_source_url(int stage)
    {
        var source = new PriceAdapter();
        var request = new PriceHistoryRequest([new Symbol("AAPL")],
            new(new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2)));
        DataEnvelope<IReadOnlyList<Bar>> result;
        DataProvenance original;
        using (var host = MetadataHost(source, true))
        {
            var pipeline = host.GetRequiredService<IDataPipeline>();
            result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request).ConfigureAwait(true);
            original = result.Provenance.Should().ContainSingle().Subject;
            original.SourceUrl.Should().Be("https://prices.test/history", "live fetch retains its source link");
            if (stage == 1)
            {
                result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request).ConfigureAwait(true);
            }
        }

        if (stage == 2)
        {
            var refuse = new PriceAdapter { Refuse = true };
            using var host = MetadataHost(refuse, true);
            result = await host.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<PriceHistoryRequest, Bar>(request).ConfigureAwait(true);
            refuse.Requests.Should().BeEmpty();
        }

        result.Payload.Should().ContainSingle().Which.Open.Should().Be(100m);
        result.Provenance.Should().ContainSingle().Which.Should().Be(stage == 0
            ? original
            : original with { RetrievalMode = RetrievalMode.Cache, SourceUrl = null });
        source.Requests.Should().ContainSingle();
    }

    private ServiceProvider MetadataHost(PriceAdapter source, bool useTimeSeriesStore,
        CapabilityFailureAdapter? primary = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_directory, "snapshots"),
            rawDocumentDirectory: Path.Combine(_directory, "raw"),
            checkpointDirectory: Path.Combine(_directory, "checkpoints"),
            eventLogDirectory: Path.Combine(_directory, "events"),
            timeSeriesDirectory: Path.Combine(_directory, "timeseries"),
            configurePipeline: options =>
            {
                options.UseTimeSeriesStore = useTimeSeriesStore;
                options.L1CacheTtl = TimeSpan.Zero;
                options.TimeSeriesStalenessMaxAge = TimeSpan.MaxValue;
            });
        if (primary is not null)
        {
            services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(primary);
        }

        services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(source);
        return services.BuildServiceProvider();
    }

    private sealed class CapabilityFailureAdapter(bool cancel)
        : IDataSourceAdapter<PriceHistoryRequest, Bar>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new("capability-failure");
        public int Priority => 100;
        public bool CanHandle(PriceHistoryRequest request) => cancel
            ? throw new OperationCanceledException("capability cancelled")
            : throw new InvalidOperationException("capability failed");

        public Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(
            PriceHistoryRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the failing capability must prevent fetch");
    }

    private sealed class WholePricePartitioner : IRequestPartitioner<PriceHistoryRequest>
    {
        public int Calls { get; private set; }

        public IReadOnlyList<PriceHistoryRequest> Partition(PriceHistoryRequest request)
        {
            Calls++;
            return [request];
        }
    }

    private sealed class PriceAdapter : IDataSourceAdapter<PriceHistoryRequest, Bar>
    {
        public ProviderCode ProviderKey { get; } = new("r2-prices");
        public bool Refuse { get; init; }
        public List<PriceHistoryRequest> Requests { get; } = [];
        public bool CanHandle(PriceHistoryRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(
            PriceHistoryRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Refuse)
            {
                throw new InvalidOperationException("unexpected fetch during durable replay");
            }

            IReadOnlyList<Bar> bars = request.Symbols.Select(symbol =>
            {
                var price = symbol.Ticker == "AAPL" ? 100m : 101m;
                return new Bar(request.Range.From, price, price, price, price, price, 1000);
            }).ToList();
            return Task.FromResult(new DataEnvelope<IReadOnlyList<Bar>>(
                bars, new DataCoverage(bars.Count, bars.Count, 0, 1m), [],
                [new DataProvenance(ProviderKey, request.DatasetKey, LicenseType.Free,
                    RetrievalMode.Api, FreshnessClass.EndOfDay,
                    new DateTimeOffset(2024, 1, 2, 20, 0, 0, TimeSpan.Zero), "https://prices.test/history")]));
        }
    }
}
