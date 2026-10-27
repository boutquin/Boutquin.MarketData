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
using Boutquin.MarketData.Abstractions.Observability;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.MarketData.Orchestration.Pipeline;

using Boutquin.Storage.Domain.Interfaces.TimeSeries;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// Gap B regression suite (spec <c>feature-timeseries-per-row-discriminant-fanout.md</c>, M-1): a
/// single-curve <see cref="YieldCurveQuoteRequest"/> that legitimately returns <b>N co-dated
/// tenors</b> must fan out on the gap-fill append path into <b>N per-tenor</b>
/// <see cref="YieldCurveQuoteDatasetKey"/> series (body-<c>Tenor</c> discriminant), and a
/// curve-as-of read must merge back across those N keys. Before the fix
/// <c>TimeSeriesPipelineExecutor.RunStoreFlowAsync</c> built ONE blanked-<c>Tenor</c> key and
/// appended the whole fetched list under it, so the co-dated rows collapsed to 1 under
/// <c>(DatasetKey, Date)</c> last-writer-wins (F-integ-01).
/// </summary>
/// <remarks>
/// This is <b>Gap B</b> (per-row fan-out WITHIN one single-curve request) — distinct from the
/// already-fixed <b>Gap A</b> (request-level multi-symbol/multi-pair partitioning) covered by
/// <c>MultiPartitionGapFillTests</c>.
/// </remarks>
public sealed class YieldCurveFanOutTests : IDisposable
{
    private const string ProviderName = "default";
    private const string CurveName = "CAD-ZERO";

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-yieldfanout-{Guid.NewGuid():N}");

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

    private static readonly DateOnly s_asOf = new(2024, 1, 2);

    private static YieldCurveQuoteRequest CurveRequest(DateOnly asOf)
        => new(new YieldCurveId(CurveName), asOf);

    private static YieldCurveQuoteDatasetKey Key(string tenor)
        => new(ProviderName, CurveName, tenor);

    private static async Task<List<YieldCurveQuote>> ReadKeyAsync(
        ITimeSeriesStore<YieldCurveQuoteDatasetKey, YieldCurveQuote> store,
        string tenor, DateOnly from, DateOnly to)
    {
        var list = new List<YieldCurveQuote>();
        await foreach (var r in store.GetRangeAsync(Key(tenor), from, to))
        {
            list.Add(r);
        }

        return list;
    }

    // A curve adapter that emits one YieldCurveQuote per (Tenor, Rate) supplied, all dated on the
    // request's as-of date. Counts fetches so a covered re-read can be shown NOT to re-dispatch.
    private sealed class CurveAdapter(IReadOnlyList<(string Tenor, decimal Rate)> nodes)
        : IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>, IPrioritizedAdapter
    {
        public int FetchCount { get; private set; }

        public ProviderCode ProviderKey { get; } = new("boc");
        public int Priority => 100;
        public bool CanHandle(YieldCurveQuoteRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<YieldCurveQuote>>> FetchAsync(
            YieldCurveQuoteRequest request, CancellationToken cancellationToken = default)
        {
            FetchCount++;
            var recs = nodes
                .Select(n => new YieldCurveQuote(request.Range.From, n.Tenor, n.Rate))
                .ToList();
            return Task.FromResult(Envelope<YieldCurveQuote>(recs, ProviderKey.Value, request.DatasetKey));
        }
    }

    // ---- AC-F1: N co-dated tenors → N rows, each under its own (…, Tenor) key --------------------

    [Fact]
    public async Task Unicode_tenor_tail_survives_live_and_cache_reads()
    {
        var nodes = new (string, decimal)[] { ("1Y", 0.1m), ("\uFFFFA", 0.2m), ("\U0001F600", 0.3m) };
        var adapter = new CurveAdapter(nodes);
        using var sp = BuildKernel(
            services => services.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(adapter),
            options =>
            {
                options.UseTimeSeriesStore = true;
                options.L1CacheTtl = TimeSpan.Zero;
            });
        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var expected = nodes.Select(node => new YieldCurveQuote(s_asOf, node.Item1, node.Item2));

        var live = await pipeline.ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(s_asOf));
        live.Payload.Should().BeEquivalentTo(expected);
        var cached = await pipeline.ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(s_asOf));
        cached.Payload.Should().BeEquivalentTo(expected);
        adapter.FetchCount.Should().Be(1);
    }

    [Fact]
    public async Task AcF1_YieldCurve_gap_fill_returns_all_tenors_each_under_own_key()
    {
        var nodes = new (string, decimal)[]
        {
            ("3M", 0.045m), ("1Y", 0.043m), ("10Y", 0.041m), ("30Y", 0.040m),
        };
        var adapter = new CurveAdapter(nodes);
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var result = await pipeline
            .ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(s_asOf))
            .ConfigureAwait(true);

        // Every tenor must survive the gap-fill append — not collapse to the last writer.
        result.Payload.Should().HaveCount(4, "each of the 4 co-dated tenors is its own series");
        foreach (var (tenor, rate) in nodes)
        {
            result.Payload.Should().Contain(
                q => q.Tenor == tenor && q.Rate == rate, $"tenor {tenor} must survive");
        }

        // Definitive per-key probe: 10Y's own dataset key must hold 10Y's own node, proving the
        // fan-out keyed each tenor separately rather than under one blanked-Tenor key.
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<YieldCurveQuoteDatasetKey, YieldCurveQuote>()!;
        var tenTx = await ReadKeyAsync(store, "10Y", s_asOf, s_asOf).ConfigureAwait(true);
        tenTx.Should().ContainSingle().Which.Rate.Should()
            .Be(0.041m, "the (…, 10Y) key must hold only 10Y's node");
    }

    // ---- AC-F2: per-tenor coverage/gap detection is independent ----------------------------------

    [Fact]
    public async Task AcF2_per_tenor_gap_is_detected_and_healed_while_covered_curve_is_not_refetched()
    {
        // Two as-of dates so both tenors are KNOWN across the curve's history; L1 off so every read
        // reaches the store tier (an L1 hit would mask the store's per-tenor coverage).
        var d1 = new DateOnly(2024, 1, 2);
        var d2 = new DateOnly(2024, 1, 3);
        var adapter = new CurveAdapter([("3M", 0.045m), ("10Y", 0.041m)]);
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(adapter),
            opt =>
            {
                opt.UseTimeSeriesStore = true;
                opt.L1CacheTtl = TimeSpan.Zero;
            });

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        // Populate both dates — each a full 2-tenor curve.
        _ = await pipeline.ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(d1))
            .ConfigureAwait(true);
        _ = await pipeline.ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(d2))
            .ConfigureAwait(true);
        adapter.FetchCount.Should().Be(2, "each of the two as-of dates dispatched once");

        // A fully-covered curve must NOT re-dispatch: re-reading d2 (both tenors present) fetches nothing.
        var covered = await pipeline.ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(d2))
            .ConfigureAwait(true);
        covered.Payload.Should().HaveCount(2, "the covered curve reads back in full");
        adapter.FetchCount.Should().Be(2, "a covered curve must not force a re-fetch (AC-F2 no over-fetch)");

        // Omit ONE tenor's coverage at d2 (10Y stays known via its d1 node). This is a per-discriminant
        // gap: 10Y has a hole at d2 while 3M is fully covered.
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<YieldCurveQuoteDatasetKey, YieldCurveQuote>()!;
        await store.DeleteRangeAsync(Key("10Y"), d2, d2).ConfigureAwait(true);

        // Re-executing d2 must detect 10Y's per-tenor gap and heal it — NOT treat the curve as covered
        // just because 3M still has a d2 row. The current per-request "does the curve have ANY row?"
        // check leaves 10Y unhealed; per-discriminant gap detection re-dispatches and restores it.
        var healed = await pipeline.ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(d2))
            .ConfigureAwait(true);
        healed.Payload.Should().HaveCount(2, "the per-tenor gap in 10Y@d2 is detected and re-fetched");
        healed.Payload.Should().Contain(q => q.Tenor == "10Y", "the missing 10Y tenor is healed");
        adapter.FetchCount.Should().Be(3, "a gap in one known tenor forces exactly one re-dispatch");

        // 10Y's d2 node is restored under its own key; 3M's history is untouched by the heal.
        (await ReadKeyAsync(store, "10Y", d2, d2).ConfigureAwait(true))
            .Should().ContainSingle("10Y@d2 is back under its own key after the heal");
        (await store.GetGapsAsync(Key("3M"), new DateRange(d1, d2)).ConfigureAwait(true))
            .Should().BeEmpty("the covered 3M tenor keeps its full coverage");
    }

    // ---- AC-F3: cross-key read merge returns tenors in §2.6.1 (dataset-key) sort order -----------

    [Fact]
    public async Task AcF3_read_merge_returns_tenors_in_dataset_key_sort_order()
    {
        // Deliberately scrambled fetch order — the merge must impose the key ordering, not echo it.
        var scrambled = new (string Tenor, decimal Rate)[]
        {
            ("10Y", 0.041m), ("3M", 0.045m), ("30Y", 0.040m), ("1Y", 0.043m), ("2Y", 0.042m),
        };
        var adapter = new CurveAdapter(scrambled);
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var result = await pipeline
            .ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(s_asOf))
            .ConfigureAwait(true);

        result.Payload.Should().HaveCount(5);

        // §2.6.1: a (provider, curveId, *) prefix scan enumerates the tenors contiguously in
        // YieldCurveQuoteDatasetKey order. The merged read must return them in exactly that order.
        var returnedTenors = result.Payload.Select(q => q.Tenor).ToList();
        var expectedOrder = scrambled
            .Select(n => n.Tenor)
            .OrderBy(Key)
            .ToList();
        returnedTenors.Should().Equal(expectedOrder,
            "the cross-key merge returns tenors in dataset-key sort order");
    }

    // ---- AC-F6: idempotency + DataQualityIncident on a residual collapse ------------------------

    [Fact]
    public async Task AcF6_fanout_is_idempotent_across_reruns()
    {
        var nodes = new (string, decimal)[] { ("3M", 0.045m), ("1Y", 0.043m), ("10Y", 0.041m) };
        var adapter = new CurveAdapter(nodes);
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(adapter),
            opt =>
            {
                opt.UseTimeSeriesStore = true;
                // Force the second call to re-dispatch (re-fetch the same N rows) rather than serve
                // from L1, so the store's per-discriminant last-writer-wins dedup is exercised.
                opt.L1CacheTtl = TimeSpan.Zero;
                opt.TimeSeriesStalenessMaxAge = TimeSpan.Zero;
            });

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var first = await pipeline.ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(s_asOf))
            .ConfigureAwait(true);
        first.Payload.Should().HaveCount(3);

        // Re-running the fan-out over the unchanged 3-row set must leave the read at 3 rows, not 6:
        // each discriminant key dedups (…, Tenor, Date) last-writer-wins.
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<YieldCurveQuoteDatasetKey, YieldCurveQuote>()!;

        var second = await pipeline.ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(s_asOf))
            .ConfigureAwait(true);
        adapter.FetchCount.Should().Be(2, "both calls must append through the adapter path");
        second.Payload.Should().HaveCount(3, "idempotent fan-out returns N rows, never 2N");

        (await ReadKeyAsync(store, "10Y", s_asOf, s_asOf).ConfigureAwait(true))
            .Should().ContainSingle("10Y's key holds one node after two identical fan-outs");
    }

    // A capturing sink so the collapse diagnostic (SPEC-OBS-01) is observable in-test.
    private sealed class CapturingIncidentSink : IDataQualityIncidentSink
    {
        public List<DataQualityIncident> Incidents { get; } = [];

        public void Report(DataQualityIncident incident) => Incidents.Add(incident);
    }

    [Fact]
    public async Task AcF6_collapse_within_one_fetch_logs_a_data_quality_incident()
    {
        // Two rows share (Tenor=10Y, Date) — a residual per-discriminant collision. The fan-out
        // must keep one (last-writer-wins) AND emit a DataQualityIncident naming the collapsed key.
        var adapter = new CurveAdapter(
        [
            ("3M", 0.045m), ("10Y", 0.041m), ("10Y", 0.0415m),
        ]);
        var sink = new CapturingIncidentSink();
        using var sp = BuildKernel(
            svc =>
            {
                svc.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(adapter);
                svc.AddSingleton<IDataQualityIncidentSink>(sink);
            },
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var result = await pipeline
            .ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(s_asOf))
            .ConfigureAwait(true);

        result.Payload.Should().HaveCount(2, "the duplicate 10Y row collapses last-writer-wins");
        result.Payload.Should().ContainSingle(row => row.Tenor == "10Y")
            .Which.Rate.Should().Be(0.0415m, "the last duplicate rate must survive");
        sink.Incidents.Should().ContainSingle("a residual collapse must be observable")
            .Which.Detail.Should().Contain("10Y");
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    // ---- F-stale-fanout-01: TimeSeriesStalenessMaxAge must fire on the fan-out path --------------
    //
    // Regression for the accepted follow-up from build-arena run 2026-07-10-2223-b90238: fan-out rows
    // live ONLY under fully-populated per-tenor keys, never under the blanked-Tenor dataset key, so the
    // single-row staleness probe (GetTailAsync on the blanked key) always found nothing and silently
    // disabled TimeSeriesStalenessMaxAge for every fan-out record type. The fix reuses the newest as-of
    // the per-discriminant cross-key scan already discovers and applies the staleness rule to it.
    //
    // Setup mirrors the single-row AC-6 staleness test: pre-populate a fully-covered curve directly
    // under its per-tenor keys at an as-of date, then request that same as-of under a clock far past the
    // staleness window. Per-tenor gap detection sees the curve as covered (zero gaps); ONLY staleness
    // can force the re-dispatch. Before the fix the adapter was never called; after it, the stale tenor
    // as-of is re-fetched exactly once and the envelope is Partial.
    [Fact]
    public async Task F_stale_fanout_01_stale_curve_tail_refetches_via_per_discriminant_newest()
    {
        var oldAsOf = new DateOnly(2026, 6, 20);
        var adapter = new CurveAdapter([("3M", 0.055m), ("10Y", 0.051m)]);
        // Clock is ~6 months past the newest stored as-of, well beyond the 7-day staleness window.
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero) };

        using var sp = BuildKernel(
            svc =>
            {
                svc.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(adapter);
                svc.AddSingleton<IClock>(clock);
            },
            opt =>
            {
                opt.UseTimeSeriesStore = true;
                opt.TimeSeriesStalenessMaxAge = TimeSpan.FromDays(7);
            });

        // Pre-populate a fully-covered 2-tenor curve DIRECTLY under each per-tenor key (not through the
        // pipeline) so per-tenor gap detection reports zero gaps for the request as-of — staleness is
        // then the only path that can trigger a dispatch.
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<YieldCurveQuoteDatasetKey, YieldCurveQuote>()!;
        await store.AppendAsync(Key("3M"), [new YieldCurveQuote(oldAsOf, "3M", 0.045m)]).ConfigureAwait(true);
        await store.AppendAsync(Key("10Y"), [new YieldCurveQuote(oldAsOf, "10Y", 0.041m)]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var result = await pipeline
            .ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(oldAsOf))
            .ConfigureAwait(true);

        adapter.FetchCount.Should().Be(1,
            "a stale curve tail must re-fetch even under full per-tenor date coverage (staleness on the "
            + "fan-out path was silently inert before the fix)");
        // A yield-curve request's range is a single as-of day, so the stale tail spans the WHOLE range:
        // the gap covers every requested day, which the executor classifies Live (a full live fetch),
        // not Partial. The load-bearing assertion is that staleness fired at all (FetchCount above); the
        // key negative is that the curve is NOT served as stale Cache.
        result.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Live,
            "a full-range stale-tail re-fetch is Live, and never stale Cache");
        // The re-fetched curve replaces the stale nodes last-writer-wins; both tenors read back.
        result.Payload.Should().HaveCount(2);
        result.Payload.Should().Contain(q => q.Tenor == "3M" && q.Rate == 0.055m,
            "the stale 3M node is refreshed to the adapter's newer rate");
        result.Payload.Should().Contain(q => q.Tenor == "10Y" && q.Rate == 0.051m,
            "the stale 10Y node is refreshed to the adapter's newer rate");
    }

    // Companion guard: with TimeSeriesStalenessMaxAge unset (the default), a fully-covered fan-out curve
    // must NOT re-dispatch — proving the fix adds no spurious fetch when staleness is disabled.
    [Fact]
    public async Task F_stale_fanout_01_disabled_staleness_does_not_refetch_covered_curve()
    {
        var oldAsOf = new DateOnly(2026, 6, 20);
        var adapter = new CurveAdapter([("3M", 0.055m), ("10Y", 0.051m)]);
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero) };

        using var sp = BuildKernel(
            svc =>
            {
                svc.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(adapter);
                svc.AddSingleton<IClock>(clock);
            },
            opt => opt.UseTimeSeriesStore = true); // TimeSeriesStalenessMaxAge left null (default)

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<YieldCurveQuoteDatasetKey, YieldCurveQuote>()!;
        await store.AppendAsync(Key("3M"), [new YieldCurveQuote(oldAsOf, "3M", 0.045m)]).ConfigureAwait(true);
        await store.AppendAsync(Key("10Y"), [new YieldCurveQuote(oldAsOf, "10Y", 0.041m)]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var result = await pipeline
            .ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(oldAsOf))
            .ConfigureAwait(true);

        adapter.FetchCount.Should().Be(0,
            "with staleness disabled a fully-covered curve is served from the store, no re-dispatch");
        result.Payload.Should().HaveCount(2, "the covered curve reads back from the store unchanged");
    }

    // ---- The persisted key must not depend on whether normalization ran (F-L6C1-01) --------------

    [Fact]
    public async Task Turning_normalizers_on_over_a_populated_store_does_not_fork_the_tenor_series()
    {
        // Tenor is BOTH a record body column and this fan-out's dataset-key discriminant, and
        // YieldCurveQuoteNormalizer rewrites it (Trim().ToUpperInvariant()). Before the fix the raw
        // era persisted key "10y" and the normalized era persisted key "10Y" for one logical tenor.
        // Both keys sit inside the prefix scan and both satisfy KeyMatchesRequest (which does not
        // filter the discriminant), so the read silently merges two parallel series at two unit
        // scales, and the raw-era key is never re-fetched. The projected key must therefore be
        // invariant under normalization. Sibling of the FactorObservation case in FactorFanOutTests.
        var later = s_asOf.AddDays(1);

        // Era 1 — RegisterDefaultNormalizers off (the shipped default): raw rows, raw tenor label.
        var rawEraAdapter = new CurveAdapter([("10y", 4.1m)]);
        using (var rawEra = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(rawEraAdapter),
            opt => opt.UseTimeSeriesStore = true))
        {
            await rawEra.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(s_asOf))
                .ConfigureAwait(true);
        }

        // Era 2 — same segment folder, normalizers now ON, one new as-of date to gap-fill.
        var normalizedEraAdapter = new CurveAdapter([("10y", 4.1m)]);
        using var normalizedEra = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(normalizedEraAdapter),
            opt =>
            {
                opt.UseTimeSeriesStore = true;
                opt.RegisterDefaultNormalizers = true;
            });

        await normalizedEra.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(later))
            .ConfigureAwait(true);

        var store = normalizedEra.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<YieldCurveQuoteDatasetKey, YieldCurveQuote>()!;

        (await ReadKeyAsync(store, "10Y", s_asOf, later).ConfigureAwait(true))
            .Should().HaveCount(2,
                "both eras of one logical tenor must land under one canonical dataset key");
        (await ReadKeyAsync(store, "10y", s_asOf, later).ConfigureAwait(true))
            .Should().BeEmpty(
                "the raw-era spelling must not be a second, permanently-stale parallel series");
    }

    // ---- Upgrade safety: a store written by a pre-canonicalisation build (F-L6C1-01 R2) ----------

    [Fact]
    public async Task A_store_written_under_the_pre_canonicalisation_key_encoding_fails_fast()
    {
        // Sibling of the FactorObservation case. A build BEFORE the discriminant canonicalisation
        // projected the key column straight off the record body, so a feed publishing a lowercase
        // tenor label persisted under "10y". This build projects "10Y". Left undetected the upgrade
        // alone forks one logical tenor across two persisted series: the legacy key is never
        // re-fetched, the canonical key reports the whole history as a gap and re-fetches it, and
        // the discriminant-blind membership filter merges both — so every date is returned twice.
        var later = s_asOf.AddDays(1);

        // Era 1 — a legacy directory: rows written under the raw discriminant, exactly as the
        // pre-canonicalisation Project produced them for this feed.
        using (var legacyEra = BuildKernel(_ => { }, opt => opt.UseTimeSeriesStore = true))
        {
            var legacyStore = legacyEra.GetRequiredService<ITimeSeriesStoreRegistry>()
                .Resolve<YieldCurveQuoteDatasetKey, YieldCurveQuote>()!;
            await legacyStore
                .AppendAsync(Key("10y"), [new YieldCurveQuote(s_asOf, "10y", 4.1m)])
                .ConfigureAwait(true);
        }

        // Era 2 — same segment folder, current build, one new as-of date that would gap-fill.
        var adapter = new CurveAdapter([("10y", 4.1m)]);
        using var currentEra = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var act = async () => await currentEra.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(CurveRequest(later))
            .ConfigureAwait(true);

        var thrown = await act.Should().ThrowAsync<MarketDataStorageException>(
            "a stored key in the pre-canonicalisation encoding must be reported, never silently "
            + "re-keyed into a second parallel series")
            .ConfigureAwait(true);

        thrown.Which.Message.Should().Contain("10y", "the message must name the stored key");
        thrown.Which.Message.Should().Contain("10Y", "and the encoding this build would write");
        thrown.Which.Message.Should().Contain(
            "time-series-cache.md", "and point at the delete-and-re-ingest migration section");

        // Nothing was appended under the new encoding — the guard fires before the gap fill.
        var store = currentEra.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<YieldCurveQuoteDatasetKey, YieldCurveQuote>()!;
        (await ReadKeyAsync(store, "10Y", s_asOf, later).ConfigureAwait(true))
            .Should().BeEmpty("the fail-fast must precede any re-keyed append");
    }
}
