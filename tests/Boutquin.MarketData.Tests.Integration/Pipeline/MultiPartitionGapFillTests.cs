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
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.MarketData.Orchestration.Pipeline;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// Gap A regression suite (run 2026-07-07-1539-b90683, round 3): a request that maps to MORE THAN ONE
/// dataset key must be partitioned per partition key BEFORE the single-identity time-series gap-fill
/// dispatch, exactly as the flag-off partition path already does. Before the fix
/// <see cref="DataPipeline.ExecuteAsync{TRequest, TRecord}"/> (Step 2b) dispatched the time-series
/// executor on the WHOLE un-partitioned request, so a multi-symbol
/// <see cref="PriceHistoryRequest"/> silently cross-attributed every symbol's bars to
/// <c>Symbols[0]</c>'s key (F-coverage-01, data corruption) and a multi-pair
/// <see cref="FxHistoryRequest"/> crashed with an unhandled <see cref="ArgumentException"/> from the
/// single-pair-guarding key builder (F-coverage-02 / F-integ-02).
/// </summary>
public sealed class MultiPartitionGapFillTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-multipart-{Guid.NewGuid():N}");

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
                // Best-effort cleanup; a still-open segment handle must not fail an asserted test.
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

    // A single business day (Tuesday) — one bar/rate per symbol/pair collides ONLY when several land
    // under the same (dataset, date) key, which is exactly the un-partitioned-batch failure mode.
    private static readonly DateRange s_oneDay =
        new(new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2));

    // ---- F-coverage-01 (🔴 silent cross-attribution) --------------------------------------------

    // Distinct Open per symbol so a bar's provenance is identifiable by value: whichever symbol's key
    // a bar lands under, its Open reveals which symbol actually produced it.
    private static readonly IReadOnlyDictionary<string, decimal> s_openPerTicker =
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["AAPL"] = 100m,
            ["MSFT"] = 101m,
        };

    private sealed class PerSymbolBarAdapter : IDataSourceAdapter<PriceHistoryRequest, Bar>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new("tiingo");
        public int Priority => 100;
        public bool CanHandle(PriceHistoryRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(
            PriceHistoryRequest request, CancellationToken cancellationToken = default)
        {
            // One bar per requested symbol, dated on the requested range's first day. Under the
            // un-partitioned batch the request carries BOTH symbols and both bars share the date, so
            // appending them under Symbols[0]'s single key collapses them (last-writer-wins).
            var bars = request.Symbols
                .Select(s => new Bar(
                    request.Range.From,
                    s_openPerTicker[s.Ticker],
                    s_openPerTicker[s.Ticker],
                    s_openPerTicker[s.Ticker],
                    s_openPerTicker[s.Ticker],
                    s_openPerTicker[s.Ticker],
                    1_000))
                .ToList();
            return Task.FromResult(Envelope<Bar>(bars, ProviderKey.Value, request.DatasetKey));
        }
    }

    [Fact]
    public async Task MultiSymbol_PriceHistory_gap_fill_attributes_each_symbol_to_its_own_key()
    {
        var adapter = new PerSymbolBarAdapter();
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var aapl = new Symbol("AAPL");
        var msft = new Symbol("MSFT");
        var batch = new PriceHistoryRequest([aapl, msft], s_oneDay);

        // Multi-symbol gap-fill: both symbols must be persisted, each under its OWN dataset key.
        var batchResult = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(batch)
            .ConfigureAwait(true);

        batchResult.Payload.Should().Contain(b => b.Open == 100m, "AAPL's bar must survive");
        batchResult.Payload.Should().Contain(b => b.Open == 101m, "MSFT's bar must survive");

        // The definitive corruption probe: read AAPL back alone. Its key must hold AAPL's OWN bar
        // (Open=100), not MSFT's (Open=101) cross-attributed by the Symbols[0] collision.
        var aaplBack = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
                new PriceHistoryRequest([aapl], s_oneDay))
            .ConfigureAwait(true);

        aaplBack.Payload.Should().ContainSingle().Which.Open.Should()
            .Be(100m, "AAPL's dataset key must return AAPL's own bar, not another symbol's");

        // And MSFT back alone must hold MSFT's own bar.
        var msftBack = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
                new PriceHistoryRequest([msft], s_oneDay))
            .ConfigureAwait(true);

        msftBack.Payload.Should().ContainSingle().Which.Open.Should()
            .Be(101m, "MSFT's dataset key must return MSFT's own bar");
    }

    // ---- F-coverage-02 / F-integ-02 (🟠 unhandled crash) ----------------------------------------

    private static readonly IReadOnlyDictionary<FxPair, decimal> s_ratePerPair =
        new Dictionary<FxPair, decimal>
        {
            [new FxPair(Currency.EUR, Currency.USD)] = 1.0845m,
            [new FxPair(Currency.USD, Currency.CAD)] = 1.3210m,
        };

    private sealed class PerPairFxAdapter : IDataSourceAdapter<FxHistoryRequest, FxRate>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new("frankfurter");
        public int Priority => 100;
        public bool CanHandle(FxHistoryRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<FxRate>>> FetchAsync(
            FxHistoryRequest request, CancellationToken cancellationToken = default)
        {
            var rates = request.Pairs
                .Select(p => new FxRate(request.Range.From, s_ratePerPair[p]))
                .ToList();
            return Task.FromResult(Envelope<FxRate>(rates, ProviderKey.Value, request.DatasetKey));
        }
    }

    [Fact]
    public async Task MultiPair_FxHistory_gap_fill_does_not_throw_and_returns_all_pairs()
    {
        var adapter = new PerPairFxAdapter();
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<FxHistoryRequest, FxRate>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = new FxHistoryRequest(
            [new FxPair(Currency.EUR, Currency.USD), new FxPair(Currency.USD, Currency.CAD)],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31)));

        DataEnvelope<IReadOnlyList<FxRate>>? result = null;
        var act = async () =>
            result = await pipeline.ExecuteAsync<FxHistoryRequest, FxRate>(request).ConfigureAwait(true);

        await act.Should().NotThrowAsync(
            "a multi-pair FxHistoryRequest must partition per pair before the single-pair key builder");

        result!.Payload.Should().Contain(r => r.Rate == 1.0845m, "EUR/USD's gap-filled rate is returned");
        result!.Payload.Should().Contain(r => r.Rate == 1.3210m, "USD/CAD's gap-filled rate is returned");
    }
}
