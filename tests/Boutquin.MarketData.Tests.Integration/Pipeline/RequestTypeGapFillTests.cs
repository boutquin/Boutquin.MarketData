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
using Boutquin.MarketData.Abstractions.Exceptions;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.MarketData.Orchestration.Pipeline;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// F-cache-abs regression suite: drive the §2.3 time-series gap-fill flow through the REAL
/// <see cref="ServiceCollectionExtensions.AddMarketDataKernel"/> composition root for request types
/// OTHER than <c>PriceHistoryRequest</c>. <c>AddMarketDataKernel</c> registers a live store and a
/// dataset-key builder for all seven request types, so the moment <c>UseTimeSeriesStore = true</c> and
/// the store has a coverage gap (guaranteed on a fresh store), the pipeline calls
/// <c>request.WithRange(gap)</c>. Before the fix that threw <see cref="NotSupportedException"/> for every
/// type except <c>PriceHistoryRequest</c> because the <c>IDataRequest.WithRange</c> default-interface
/// method throws and only <c>PriceHistoryRequest</c> overrode it. Each test proves the gap-fill runs
/// end-to-end (adapter dispatched for the gap, fetched records merged into the payload) rather than
/// crashing on the first gap.
/// </summary>
public sealed class RequestTypeGapFillTests : IDisposable
{
    [Fact]
    public async Task Futures_months_survive_same_day_fetch_and_restart()
    {
        var date = new DateOnly(2024, 1, 2);
        var rows = new[]
        {
            new FuturesSettlement(date, new FuturesProductCode("SR3"), new ContractMonth(2026, 6), 95m, 0.05m),
            new FuturesSettlement(date, new FuturesProductCode("SR3"), new ContractMonth(2026, 9), 96m, 0.04m)
        };
        var adapter = new CountingAdapter<FuturesSettlementRequest, FuturesSettlement>("futures", _ => rows);
        var request = new FuturesSettlementRequest(new FuturesProductCode("SR3"), new DateRange(date, date));
        using (var writer = BuildKernel(
            services => services.AddSingleton<IDataSourceAdapter<FuturesSettlementRequest, FuturesSettlement>>(adapter),
            options =>
            {
                options.UseTimeSeriesStore = true;
                options.L1CacheTtl = TimeSpan.Zero;
                options.TimeSeriesStalenessMaxAge = TimeSpan.MaxValue;
            }))
        {
            var result = await writer.GetRequiredService<IDataPipeline>().ExecuteAsync<FuturesSettlementRequest, FuturesSettlement>(request);
            result.Payload.Should().BeEquivalentTo(rows, "each delivery month is an independent same-day series");
        }

        using var reader = BuildKernel(_ => { }, options =>
        {
            options.UseTimeSeriesStore = true;
            options.TimeSeriesStalenessMaxAge = TimeSpan.MaxValue;
        });
        var recovered = await reader.GetRequiredService<IDataPipeline>().ExecuteAsync<FuturesSettlementRequest, FuturesSettlement>(request);
        recovered.Payload.Should().BeEquivalentTo(rows, "restart must not require an adapter to recover either month");
    }

    [Fact]
    public async Task Futures_legacy_blank_month_fails_before_merging_lossy_data()
    {
        var date = new DateOnly(2024, 1, 2);
        using var kernel = BuildKernel(_ => { }, options => options.UseTimeSeriesStore = true);
        var store = kernel.GetRequiredService<ITimeSeriesStoreRegistry>().Resolve<FuturesSettlementDatasetKey, FuturesSettlement>()!;
        await store.AppendAsync(new FuturesSettlementDatasetKey("default", "SR3", string.Empty),
            [new FuturesSettlement(date, new FuturesProductCode("SR3"), new ContractMonth(2026, 6), 95m, 0.05m)]);
        var request = new FuturesSettlementRequest(new FuturesProductCode("SR3"), new DateRange(date, date));

        var read = () => kernel.GetRequiredService<IDataPipeline>().ExecuteAsync<FuturesSettlementRequest, FuturesSettlement>(request);
        await read.Should().ThrowAsync<MarketDataStorageException>().WithMessage("*re-ingest*");
    }

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-gapfill-{Guid.NewGuid():N}");

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
                // Best-effort cleanup — a still-open segment handle on a slow CI runner should
                // not fail the test that already asserted its behaviour.
            }
        }
    }

    private ServiceProvider BuildKernel(
        Action<IServiceCollection> configure,
        Action<DataPipelineOptions>? configurePipeline = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_tempDir, "snapshots"),
            rawDocumentDirectory: Path.Combine(_tempDir, "raw"),
            checkpointDirectory: Path.Combine(_tempDir, "checkpoints"),
            eventLogDirectory: Path.Combine(_tempDir, "events"),
            timeSeriesDirectory: Path.Combine(_tempDir, "timeseries"),
            configurePipeline: configurePipeline);
        configure(services);
        return services.BuildServiceProvider();
    }

    private static DataProvenance ApiProvenance(string provider, string dataset)
        => new(new ProviderCode(provider), dataset, LicenseType.Free, RetrievalMode.Api,
            FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, $"https://{provider}.test/{dataset}");

    private static DataEnvelope<IReadOnlyList<TRecord>> Envelope<TRecord>(
        IReadOnlyList<TRecord> recs, string provider, string dataset)
    {
        var cov = new DataCoverage(recs.Count, recs.Count, 0, recs.Count > 0 ? 1.0m : 0.0m);
        return new DataEnvelope<IReadOnlyList<TRecord>>(
            recs, cov, [], [ApiProvenance(provider, dataset)]);
    }

    private sealed class CountingAdapter<TRequest, TRecord> : IDataSourceAdapter<TRequest, TRecord>, IPrioritizedAdapter
        where TRequest : IDataRequest
    {
        private readonly Func<DateRange, IReadOnlyList<TRecord>> _forRange;

        public CountingAdapter(string provider, Func<DateRange, IReadOnlyList<TRecord>> forRange)
        {
            ProviderKey = new ProviderCode(provider);
            _forRange = forRange;
        }

        public ProviderCode ProviderKey { get; }
        public int Priority => 100;
        public bool CanHandle(TRequest request) => true;
        public List<DateRange> Dispatches { get; } = [];

        public Task<DataEnvelope<IReadOnlyList<TRecord>>> FetchAsync(
            TRequest request, CancellationToken cancellationToken = default)
        {
            Dispatches.Add(request.Range);
            var recs = _forRange(request.Range);
            return Task.FromResult(Envelope(recs, ProviderKey.Value, request.DatasetKey));
        }
    }

    // FxHistoryRequest / FxRate — a fresh store means the whole requested range is one gap.
    // The pipeline must call WithRange(gap) to fetch it. Before the fix this threw
    // NotSupportedException; after the fix the EUR/USD rates are fetched and returned.
    [Fact]
    public async Task FxHistoryRequest_gap_fill_fetches_and_returns_records()
    {
        var adapter = new CountingAdapter<FxHistoryRequest, FxRate>("frankfurter", range =>
        [
            new FxRate(range.From, 1.0845m),
            new FxRate(range.To, 1.0910m),
        ]);

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<FxHistoryRequest, FxRate>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = new FxHistoryRequest(
            [new FxPair(Currency.EUR, Currency.USD)],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)));

        var result = await pipeline.ExecuteAsync<FxHistoryRequest, FxRate>(request)
            .ConfigureAwait(true);

        adapter.Dispatches.Should().NotBeEmpty("the fresh-store gap must be gap-filled via WithRange");
        result.Payload.Should().Contain(r => r.Rate == 1.0845m, "the gap-filled FX rates are returned");
    }

    // EconomicSeriesRequest / ScalarObservation.
    [Fact]
    public async Task EconomicSeriesRequest_gap_fill_fetches_and_returns_records()
    {
        var adapter = new CountingAdapter<EconomicSeriesRequest, ScalarObservation>("fred", range =>
        [
            new ScalarObservation(range.From, 5.31m, "decimal"),
            new ScalarObservation(range.To, 5.33m, "decimal"),
        ]);

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<EconomicSeriesRequest, ScalarObservation>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = new EconomicSeriesRequest(
            new EconomicSeriesId("SOFR"),
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)));

        var result = await pipeline.ExecuteAsync<EconomicSeriesRequest, ScalarObservation>(request)
            .ConfigureAwait(true);

        adapter.Dispatches.Should().NotBeEmpty("the fresh-store gap must be gap-filled via WithRange");
        result.Payload.Should().Contain(o => o.Value == 5.31m, "the gap-filled observations are returned");
    }

    // OptionChainRequest / OptionQuote — the row (non-columnar) store path, via the §3.1 B′ keyed-row
    // envelope (M-3): the option-chain adapter emits OptionChainRow (TFetched, contract identity +
    // quote body), and the per-contract fan-out projects each to its own OptionQuoteDatasetKey and
    // stores the slim OptionQuote (TStored). This is the fetched-differs-from-stored dispatch — the
    // adapter is IDataSourceAdapter<OptionChainRequest, OptionChainRow>, not <…, OptionQuote>.
    [Fact]
    public async Task OptionChainRequest_gap_fill_fetches_and_returns_records()
    {
        var adapter = new CountingAdapter<OptionChainRequest, OptionChainRow>("opra", range =>
        [
            new OptionChainRow(
                new OptionContract(new DateOnly(2024, 6, 21), 150m, OptionType.Call),
                new OptionQuote(range.From, 1.25m, 1.30m, 1.27m, 0.22m, 100, 50)),
            new OptionChainRow(
                new OptionContract(new DateOnly(2024, 6, 21), 155m, OptionType.Put),
                new OptionQuote(range.From, 1.40m, 1.45m, 1.42m, 0.24m, 120, 60)),
        ]);

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<OptionChainRequest, OptionChainRow>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = new OptionChainRequest(
            "AAPL",
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)));

        var result = await pipeline.ExecuteAsync<OptionChainRequest, OptionQuote>(request)
            .ConfigureAwait(true);

        adapter.Dispatches.Should().NotBeEmpty("the fresh-store gap must be gap-filled via WithRange");
        result.Payload.Should().Contain(q => q.Bid == 1.25m, "the gap-filled option quotes are returned");
        result.Payload.Should().HaveCount(2, "each of the two fetched contracts is its own per-contract series");
    }
}
