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
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.MarketData.Testing.Fakes;

using Boutquin.Storage.Domain.Interfaces.TimeSeries;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// M-3 regression suite (spec <c>feature-timeseries-per-row-discriminant-fanout.md</c> §3.1, AC-F4):
/// the B′ keyed-row fetch envelope. An option-chain adapter emits <b>N co-dated
/// <see cref="OptionChainRow"/>s</b> (fetched type ≠ stored type: <c>OptionChainRow</c> vs
/// <c>OptionQuote</c>); the gap-fill append must fan each out into its own fully-populated
/// <see cref="OptionQuoteDatasetKey"/> <c>(Provider, Underlying, Expiry, Strike, Type)</c> and store
/// the slim <see cref="OptionQuote"/> body under it, and a chain-as-of read must merge back across the
/// N per-contract keys.
/// </summary>
/// <remarks>
/// This is the fetched-differs-from-stored case the executor could not serve before M-3: the pre-fix
/// executor hardcoded the fan-out to <c>IDatasetKeyFanOut&lt;…, TRecord, TRecord&gt;</c> and dispatched
/// <c>IDataSourceAdapter&lt;…, TRecord&gt;</c>, so an <c>OptionChainRow</c>-emitting adapter had no
/// matching dispatch and the chain collapsed. OptionQuote is also the first <b>row-format</b> fan-out
/// (no columnar engine), so the cross-key read merge exercises the row-store scan path.
/// </remarks>
public sealed class OptionChainFanOutTests : IDisposable
{
    private const string ProviderName = "default";
    private const string Underlying = "AAPL";

    private static readonly DateOnly s_asOf = new(2024, 1, 2);
    private static readonly DateOnly s_exp1 = new(2024, 7, 19);
    private static readonly DateOnly s_exp2 = new(2024, 8, 16);

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-optionfanout-{Guid.NewGuid():N}");

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

    // Four distinct listed contracts on one underlying, each with a distinct body (Bid) so the per-key
    // read can prove the identity lives in the KEY and the correct slim body round-trips under it.
    private static IReadOnlyList<OptionChainRow> ChainRows() =>
    [
        Row(s_exp1, 150m, OptionType.Call, bid: 12.50m),
        Row(s_exp1, 155m, OptionType.Call, bid: 9.75m),
        Row(s_exp1, 150m, OptionType.Put, bid: 6.20m),
        Row(s_exp2, 150m, OptionType.Call, bid: 14.10m),
    ];

    private static OptionChainRow Row(DateOnly expiry, decimal strike, OptionType type, decimal bid)
        => new(
            new OptionContract(expiry, strike, type),
            new OptionQuote(default, Bid: bid, Ask: bid + 0.10m, LastPrice: bid, ImpliedVolatility: 0.25m,
                OpenInterest: 100, Volume: 10));

    private static OptionQuoteDatasetKey Key(DateOnly expiry, decimal strike, OptionType type)
        => new(ProviderName, Underlying, expiry, strike, type);

    private static async Task<List<OptionQuote>> ReadKeyAsync(
        ITimeSeriesStore<OptionQuoteDatasetKey, OptionQuote> store,
        OptionQuoteDatasetKey key, DateOnly from, DateOnly to)
    {
        var list = new List<OptionQuote>();
        await foreach (var r in store.GetRangeAsync(key, from, to))
        {
            list.Add(r);
        }

        return list;
    }

    // ---- AC-F4: N-contract chain → N rows, each under its own fully-populated key ----------------

    [Fact]
    public async Task AcF4_option_chain_gap_fill_returns_all_contracts_each_under_own_key()
    {
        var adapter = new FakeOptionChainAdapter(ChainRows());
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<OptionChainRequest, OptionChainRow>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var result = await pipeline
            .ExecuteAsync<OptionChainRequest, OptionQuote>(new OptionChainRequest(Underlying, new DateRange(s_asOf, s_asOf)))
            .ConfigureAwait(true);

        // Every contract must survive the fan-out append — not collapse to the last writer under one
        // blanked-discriminant key.
        result.Payload.Should().HaveCount(4, "each of the 4 co-dated contracts is its own series");

        // Definitive per-key probes: each contract's fully-populated key holds ONLY its own slim body.
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<OptionQuoteDatasetKey, OptionQuote>()!;

        (await ReadKeyAsync(store, Key(s_exp1, 150m, OptionType.Call), s_asOf, s_asOf).ConfigureAwait(true))
            .Should().ContainSingle().Which.Bid.Should().Be(12.50m, "the (…,150,Call) key holds only its node");
        (await ReadKeyAsync(store, Key(s_exp1, 155m, OptionType.Call), s_asOf, s_asOf).ConfigureAwait(true))
            .Should().ContainSingle().Which.Bid.Should().Be(9.75m, "the (…,155,Call) key holds only its node");
        (await ReadKeyAsync(store, Key(s_exp1, 150m, OptionType.Put), s_asOf, s_asOf).ConfigureAwait(true))
            .Should().ContainSingle().Which.Bid.Should().Be(6.20m, "the (…,150,Put) key holds only its node");
        (await ReadKeyAsync(store, Key(s_exp2, 150m, OptionType.Call), s_asOf, s_asOf).ConfigureAwait(true))
            .Should().ContainSingle().Which.Bid.Should().Be(14.10m, "the (…,Aug,150,Call) key holds only its node");
    }

    // ---- AC-F6 (idempotency): twice over the unchanged chain → N rows, never 2N -------------------

    [Fact]
    public async Task AcF6_option_chain_fanout_is_idempotent_across_reruns()
    {
        var adapter = new FakeOptionChainAdapter(ChainRows());
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<OptionChainRequest, OptionChainRow>>(adapter),
            opt =>
            {
                opt.UseTimeSeriesStore = true;
                // Force the second call to re-dispatch (re-fetch the same N rows) rather than serve
                // from L1, so the store's per-contract last-writer-wins dedup is exercised.
                opt.L1CacheTtl = TimeSpan.Zero;
                opt.TimeSeriesStalenessMaxAge = TimeSpan.Zero;
            });

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = new OptionChainRequest(Underlying, new DateRange(s_asOf, s_asOf));

        var first = await pipeline.ExecuteAsync<OptionChainRequest, OptionQuote>(request).ConfigureAwait(true);
        first.Payload.Should().HaveCount(4);

        var second = await pipeline.ExecuteAsync<OptionChainRequest, OptionQuote>(request).ConfigureAwait(true);
        adapter.FetchCount.Should().Be(2, "both calls must append through the adapter path");
        second.Payload.Should().HaveCount(4, "idempotent fan-out returns N rows, never 2N");

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<OptionQuoteDatasetKey, OptionQuote>()!;
        (await ReadKeyAsync(store, Key(s_exp1, 150m, OptionType.Call), s_asOf, s_asOf).ConfigureAwait(true))
            .Should().ContainSingle("a contract's key holds one node after two identical fan-outs");
    }

    // ---- F-L5-01 / F-L7C2-01: the stored-type normalizer must run on the fan-out append path -------

    [Fact]
    public async Task OptionQuoteNormalizer_runs_on_the_option_chain_fanout_append_path_when_flag_is_on()
    {
        // A dirty row: unrounded 6dp Bid/Ask/LastPrice, negative OpenInterest/Volume, percent-form IV —
        // every field OptionQuoteNormalizer transforms. If the normalizer never runs on this append
        // path, every value below survives unchanged (this reproduces critic D's live probe, which
        // observed exactly Bid=12.123456, OpenInterest=-50, Volume=-10 surviving through
        // IDataPipeline.ExecuteAsync<OptionChainRequest, OptionQuote>).
        var dirtyRow = new OptionChainRow(
            new OptionContract(s_exp1, 150m, OptionType.Call),
            new OptionQuote(default, Bid: 12.123456m, Ask: 12.223456m, LastPrice: 12.153456m,
                ImpliedVolatility: 25.0m, OpenInterest: -50, Volume: -10));

        var adapter = new FakeOptionChainAdapter([dirtyRow]);
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<OptionChainRequest, OptionChainRow>>(adapter),
            opt =>
            {
                opt.UseTimeSeriesStore = true;
                opt.RegisterDefaultNormalizers = true;
            });

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var result = await pipeline
            .ExecuteAsync<OptionChainRequest, OptionQuote>(
                new OptionChainRequest(Underlying, new DateRange(s_asOf, s_asOf)))
            .ConfigureAwait(true);

        var quote = result.Payload.Should().ContainSingle().Subject;
        quote.Bid.Should().Be(12.1235m, "OptionQuoteNormalizer rounds prices to 4dp away from zero");
        quote.ImpliedVolatility.Should().Be(0.25m, "percent-form IV (>= 1) must convert to decimal form");
        quote.OpenInterest.Should().Be(0, "negative open interest must be clamped to zero");
        quote.Volume.Should().Be(0, "negative volume must be clamped to zero");

        // The durable store — not just the in-memory returned payload — must carry the normalized
        // values, proving the append path itself normalized rather than some other read-side step.
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<OptionQuoteDatasetKey, OptionQuote>()!;
        var stored = (await ReadKeyAsync(store, Key(s_exp1, 150m, OptionType.Call), s_asOf, s_asOf)
                .ConfigureAwait(true))
            .Should().ContainSingle().Subject;
        stored.Bid.Should().Be(12.1235m, "the durably appended row is normalized, not the raw fetched value");
    }

    // ---- Per-discriminant AC-5: a rolled-off contract's permanent hole is dispatched at most once ----

    [Fact]
    public async Task PerDiscriminant_rolled_off_contract_hole_is_not_re_dispatched_every_read()
    {
        // A contract that is KNOWN (has an early stored row) but permanently source-absent over the tail
        // of the range (expired / rolled off) must not re-dispatch the adapter on every read. The fan-out
        // gap union re-adds that per-contract hole on each read; without a per-discriminant attempted-range
        // clamp the adapter fires forever and the hole never subsumes to a cache hit. Once the chain fetch
        // over the hole comes back without the rolled-off contract, that per-contract span is recorded as
        // attempted and clamped on subsequent reads — the single-key AC-5 mechanism, keyed per discriminant.
        var liveContract = new OptionContract(s_exp2, 150m, OptionType.Call);

        // The live chain the adapter still returns — only the live contract; the s_exp1 contract has
        // rolled off and is never re-emitted, so its tail hole is genuinely source-absent.
        var adapter = new FakeOptionChainAdapter(
        [
            new OptionChainRow(liveContract,
                new OptionQuote(default, Bid: 14.10m, Ask: 14.20m, LastPrice: 14.10m,
                    ImpliedVolatility: 0.25m, OpenInterest: 100, Volume: 10)),
        ]);

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<OptionChainRequest, OptionChainRow>>(adapter),
            opt =>
            {
                opt.UseTimeSeriesStore = true;
                // Bypass L1 so each read re-evaluates store-level gaps; otherwise the second read is a
                // trivial L1 hit and the per-discriminant clamp is never exercised.
                opt.L1CacheTtl = TimeSpan.Zero;
            });

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<OptionQuoteDatasetKey, OptionQuote>()!;

        // Seed BOTH contracts with an early observation on d1 so each is a KNOWN discriminant key; neither
        // yet has the later date d2, so both carry a d2 gap on the first read.
        var d1 = s_asOf;
        var d2 = s_asOf.AddDays(1);
        await store.AppendAsync(
            Key(s_exp1, 150m, OptionType.Call),
            [new OptionQuote(d1, 6.20m, 6.30m, 6.20m, 0.25m, 100, 10)]).ConfigureAwait(true);
        await store.AppendAsync(
            Key(s_exp2, 150m, OptionType.Call),
            [new OptionQuote(d1, 13.90m, 14.00m, 13.90m, 0.25m, 100, 10)]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = new OptionChainRequest(Underlying, new DateRange(d1, d2));

        // First read: the union d2 gap dispatches the chain adapter once; the live contract fills d2, the
        // rolled-off contract stays absent (genuinely gone from the source).
        _ = await pipeline.ExecuteAsync<OptionChainRequest, OptionQuote>(request).ConfigureAwait(true);
        adapter.FetchCount.Should().Be(1, "the first read dispatches once to fill the d2 gap");

        // Second read over the same range: the rolled-off contract's d2 hole is now attempted-and-empty,
        // so it must be clamped — no second dispatch. Before the fix it re-dispatched on every read.
        _ = await pipeline.ExecuteAsync<OptionChainRequest, OptionQuote>(request).ConfigureAwait(true);
        adapter.FetchCount.Should().Be(1,
            "a permanently source-absent discriminant's hole is dispatched at most once, then subsumes");
    }
}
