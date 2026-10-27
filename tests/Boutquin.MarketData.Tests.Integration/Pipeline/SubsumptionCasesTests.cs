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
/// M-8 (§3.1.1 deliverable 16, spec §5): the five canonical subsumption cases driven through the
/// REAL <see cref="ServiceCollectionExtensions.AddMarketDataKernel"/> composition root as ONE
/// coherent parametrized set. Where M-7's <see cref="DataPipelineTimeSeriesStoreTests"/> proves
/// each gap-shape in isolation, this fixture enumerates the canonical taxonomy — full cover,
/// prefix gap, suffix gap, two gaps, disjoint — with a single table-driven contract so the
/// coverage↔dispatch invariant is asserted uniformly (exact dispatched ranges, dispatch count,
/// and resulting <see cref="RetrievalMode"/>) across every case.
/// </summary>
public sealed class SubsumptionCasesTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-subsumption-{Guid.NewGuid():N}");

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
                // Best-effort cleanup.
            }
        }
    }

    /// <summary>
    /// The canonical subsumption taxonomy. Each row: a name, the store's pre-existing coverage
    /// (or <see langword="null"/> for the disjoint/cold case where nothing overlaps), the request
    /// range, the exact adapter dispatch ranges expected, and the resulting retrieval mode.
    /// </summary>
    public enum SubsumptionCase
    {
        /// <summary>Store fully covers the request — zero dispatch, Cache.</summary>
        FullCover,

        /// <summary>Store misses only a leading span — one prefix dispatch, Partial.</summary>
        PrefixGap,

        /// <summary>Store misses only a trailing span — one suffix dispatch, Partial.</summary>
        SuffixGap,

        /// <summary>Store covers an interior window only — two dispatches (prefix + suffix), Partial.</summary>
        TwoGaps,

        /// <summary>Store's coverage is entirely disjoint from (below) the request — one full-range dispatch, Live.</summary>
        Disjoint,
    }

    [Theory]
    [InlineData(SubsumptionCase.FullCover)]
    [InlineData(SubsumptionCase.PrefixGap)]
    [InlineData(SubsumptionCase.SuffixGap)]
    [InlineData(SubsumptionCase.TwoGaps)]
    [InlineData(SubsumptionCase.Disjoint)]
    public async Task Subsumption_case_dispatches_exactly_the_gaps_and_sets_the_right_mode(SubsumptionCase which)
    {
        var (seedFrom, seedTo, reqFrom, reqTo, expectedDispatches, expectedMode) = Plan(which);

        var adapter = new CountingBarAdapter("tiingo", range => [Bar(range.From, 1m), Bar(range.To, 1m)]);

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var key = KeyFor("AGG");

        // Seed the store's pre-existing coverage (skip for the cold/disjoint-with-nothing case when
        // seedFrom is null — Disjoint still seeds a below-request island).
        if (seedFrom is { } sf && seedTo is { } st)
        {
            await store.AppendAsync(key, [Bar(sf, 100m), Bar(st, 100m)]).ConfigureAwait(true);
        }

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var result = await pipeline
            .ExecuteAsync<PriceHistoryRequest, Bar>(PriceRequest("AGG", reqFrom, reqTo))
            .ConfigureAwait(true);

        adapter.Dispatches.Should().HaveCount(expectedDispatches.Count,
            "case {0} must dispatch exactly the number of gaps", which);
        foreach (var ex in expectedDispatches)
        {
            adapter.Dispatches.Should().Contain(r => r.From == ex.From && r.To == ex.To,
                "case {0} must dispatch the gap {1}..{2}", which, ex.From, ex.To);
        }

        result.Provenance.Should().OnlyContain(p => p.RetrievalMode == expectedMode,
            "case {0} resolves to {1}", which, expectedMode);
    }

    private static (DateOnly? SeedFrom, DateOnly? SeedTo, DateOnly ReqFrom, DateOnly ReqTo,
        IReadOnlyList<DateRange> Dispatches, RetrievalMode Mode) Plan(SubsumptionCase which)
    {
        var d = (int y, int m, int day) => new DateOnly(y, m, day);
        return which switch
        {
            // Store covers the whole request → no gap, Cache.
            SubsumptionCase.FullCover => (
                d(2000, 1, 1), d(2026, 4, 18), d(2020, 1, 1), d(2026, 4, 18),
                [], RetrievalMode.Cache),

            // Store covers [2010..reqTo]; request reaches back to 2005 → one prefix gap [2005..2009-12-31].
            SubsumptionCase.PrefixGap => (
                d(2010, 1, 1), d(2026, 4, 18), d(2005, 1, 1), d(2026, 4, 18),
                [new DateRange(d(2005, 1, 1), d(2009, 12, 31))], RetrievalMode.Partial),

            // Store covers [reqFrom..2026-06-30]; request extends to 2026-07-05 → one suffix gap.
            SubsumptionCase.SuffixGap => (
                d(2026, 1, 1), d(2026, 6, 30), d(2026, 1, 1), d(2026, 7, 5),
                [new DateRange(d(2026, 7, 1), d(2026, 7, 5))], RetrievalMode.Partial),

            // Store covers interior [2010..2020]; request [2005..2026] → prefix + suffix.
            SubsumptionCase.TwoGaps => (
                d(2010, 1, 1), d(2020, 12, 31), d(2005, 1, 1), d(2026, 12, 31),
                [
                    new DateRange(d(2005, 1, 1), d(2009, 12, 31)),
                    new DateRange(d(2021, 1, 1), d(2026, 12, 31)),
                ], RetrievalMode.Partial),

            // Store's island [2000..2001] lies entirely BELOW the request [2010..2012] → the whole
            // request is one disjoint gap; nothing pre-existing overlaps → Live.
            SubsumptionCase.Disjoint => (
                d(2000, 1, 1), d(2001, 12, 31), d(2010, 1, 1), d(2012, 12, 31),
                [new DateRange(d(2010, 1, 1), d(2012, 12, 31))], RetrievalMode.Live),

            _ => throw new ArgumentOutOfRangeException(nameof(which), which, null),
        };
    }

    // M-8 (§3.1.1 deliverable 16, §2.3 step-6 note) · Adapter-rate-limit compatibility. The flag-ON
    // gap-fill loop dispatches each gap through the SAME IDataSourceAdapter fallback chain the legacy
    // path uses (§2.3: "Adapter rate-limiting and batching rules unchanged"), so an adapter-level rate
    // limiter / throttle still sees — and can gate — every gap dispatch. This proves the store path
    // does not bypass the adapter contract that rate-limiting decorates: every gap is a distinct
    // adapter FetchAsync invocation, in gap order.
    [Fact]
    public async Task Adapter_rate_limit_compatibility_every_gap_flows_through_the_adapter_chain()
    {
        var observedDispatchOrder = new List<DateOnly>();
        var adapter = new CountingBarAdapter("tiingo", range =>
        {
            // A rate limiter decorating the adapter would observe exactly these calls, in this order.
            observedDispatchOrder.Add(range.From);
            return [Bar(range.From, 1m), Bar(range.To, 1m)];
        });

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        // Interior coverage (2010..2020) so the request produces two gaps (prefix + suffix).
        await store.AppendAsync(KeyFor("AGG"),
        [
            Bar(new DateOnly(2010, 1, 1), 100m),
            Bar(new DateOnly(2020, 12, 31), 100m),
        ]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            PriceRequest("AGG", new DateOnly(2005, 1, 1), new DateOnly(2026, 12, 31))).ConfigureAwait(true);

        // Every gap reached the adapter (rate-limiting/throttle sees each one), in gap order:
        // prefix (2005) before suffix (2021). The store path never batches around or bypasses the
        // adapter contract that rate-limiting decorates.
        observedDispatchOrder.Should().Equal(
            [new DateOnly(2005, 1, 1), new DateOnly(2021, 1, 1)],
            "each gap is a distinct adapter dispatch, in gap order — rate-limit rules apply unchanged");
    }

    // ─── Shared harness (mirrors DataPipelineTimeSeriesStoreTests) ───

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

    private static PriceHistoryRequest PriceRequest(string ticker, DateOnly from, DateOnly to)
        => new([new Symbol(ticker)], new DateRange(from, to));

    private static Bar Bar(DateOnly date, decimal close)
        => new(date, close, close, close, close, close, 1_000);

    private static PriceBarDatasetKey KeyFor(string ticker)
        => new("default", DataFrequency.Daily, ticker, null);

    private static DataProvenance ApiProvenance(string provider, string dataset)
        => new(new ProviderCode(provider), dataset, LicenseType.Free, RetrievalMode.Api,
            FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, $"https://{provider}.test/{dataset}");

    private sealed class CountingBarAdapter : IDataSourceAdapter<PriceHistoryRequest, Bar>, IPrioritizedAdapter
    {
        private readonly Func<DateRange, IReadOnlyList<Bar>> _forRange;

        public CountingBarAdapter(string provider, Func<DateRange, IReadOnlyList<Bar>> forRange)
        {
            ProviderKey = new ProviderCode(provider);
            _forRange = forRange;
        }

        public ProviderCode ProviderKey { get; }
        public int Priority => 100;
        public bool CanHandle(PriceHistoryRequest request) => true;
        public List<DateRange> Dispatches { get; } = [];

        public Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(
            PriceHistoryRequest request, CancellationToken cancellationToken = default)
        {
            Dispatches.Add(request.Range);
            var recs = _forRange(request.Range);
            var cov = new DataCoverage(recs.Count, recs.Count, 0, recs.Count > 0 ? 1.0m : 0.0m);
            return Task.FromResult(new DataEnvelope<IReadOnlyList<Bar>>(
                recs, cov, [], [ApiProvenance(ProviderKey.Value, request.DatasetKey)]));
        }
    }
}
