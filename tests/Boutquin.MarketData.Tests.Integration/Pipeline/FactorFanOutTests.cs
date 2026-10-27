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
using Boutquin.MarketData.Abstractions.Diagnostics;
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
/// M-2 regression suite (spec <c>feature-timeseries-per-row-discriminant-fanout.md</c>, M-2): a
/// single <see cref="FactorSeriesRequest"/> that legitimately returns <b>N co-dated factors</b> must
/// fan out on the gap-fill append path into <b>N per-factor</b> <see cref="FactorObservationDatasetKey"/>
/// series (body-<c>FactorName</c> discriminant), and a factor-set-as-of read must merge back across
/// those N keys. This is the Factor analogue of <c>YieldCurveFanOutTests</c> (M-1): before the fix,
/// <c>FactorSeriesDatasetKeyBuilder</c> alone builds ONE blanked-<c>FactorName</c> key and the gap-fill
/// path appends the whole fetched list under it, so the co-dated factors collapse to 1 under
/// <c>(DatasetKey, Date)</c> last-writer-wins.
/// </summary>
public sealed class FactorFanOutTests : IDisposable
{
    private const string ProviderName = "default";
    private const string DatasetIdValue = "F-F_Research_Data_Factors_daily";

    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-factorfanout-{Guid.NewGuid():N}");

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

    private static FactorSeriesRequest FactorRequest(DateOnly asOf)
        => new(new FactorDatasetId(DatasetIdValue), new DateRange(asOf, asOf));

    private static FactorObservationDatasetKey Key(string factorName)
        => new(ProviderName, DatasetIdValue, DataFrequency.Daily, factorName);

    private static async Task<List<FactorObservation>> ReadKeyAsync(
        ITimeSeriesStore<FactorObservationDatasetKey, FactorObservation> store,
        string factorName, DateOnly from, DateOnly to)
    {
        var list = new List<FactorObservation>();
        await foreach (var r in store.GetRangeAsync(Key(factorName), from, to))
        {
            list.Add(r);
        }

        return list;
    }

    // A factor-set adapter that emits one FactorObservation per (FactorName, Value) supplied, all
    // dated on the request's as-of date. Counts fetches so a covered re-read can be shown NOT to
    // re-dispatch.
    private sealed class FactorAdapter(IReadOnlyList<(string FactorName, decimal Value)> nodes)
        : IDataSourceAdapter<FactorSeriesRequest, FactorObservation>, IPrioritizedAdapter
    {
        public int FetchCount { get; private set; }

        public IReadOnlyList<DataIssue> Issues { get; init; } = [];

        public LicenseType License { get; init; } = LicenseType.Free;

        public ProviderCode ProviderKey { get; } = new("famafrench");
        public int Priority => 100;
        public bool CanHandle(FactorSeriesRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<FactorObservation>>> FetchAsync(
            FactorSeriesRequest request, CancellationToken cancellationToken = default)
        {
            FetchCount++;
            var recs = nodes
                .Select(n => new FactorObservation(request.Range.From, n.FactorName, n.Value))
                .ToList();
            var envelope = Envelope<FactorObservation>(recs, ProviderKey.Value, request.DatasetKey);
            return Task.FromResult(envelope with
            {
                Issues = Issues,
                Provenance = envelope.Provenance.Select(item => item with { LicenseFlag = License }).ToArray(),
            });
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Fanout_source_metadata_survives_live_and_restart(bool restart, bool checkIssues)
    {
        var warning = new DataIssue(IssueCode.MissingField, IssueSeverity.Warning, "factor-source-warning");
        var adapter = new FactorAdapter([("MKT", 0.01m), ("SMB", 0.02m)])
        {
            Issues = [warning],
            License = LicenseType.ResearchOnly,
        };
        DataEnvelope<IReadOnlyList<FactorObservation>> result;
        using (var writer = BuildKernel(
            services => services.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(adapter),
            options =>
            {
                options.UseTimeSeriesStore = true;
                options.L1CacheTtl = TimeSpan.Zero;
                options.TimeSeriesStalenessMaxAge = TimeSpan.MaxValue;
            }))
        {
            result = await writer.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(s_asOf)).ConfigureAwait(true);
        }

        if (restart)
        {
            using var reader = BuildKernel(_ => { }, options =>
            {
                options.UseTimeSeriesStore = true;
                options.TimeSeriesStalenessMaxAge = TimeSpan.MaxValue;
            });
            result = await reader.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(s_asOf)).ConfigureAwait(true);
        }

        result.Payload.Should().HaveCount(2);
        if (checkIssues)
        {
            result.Issues.Should().Contain(warning);
        }
        else
        {
            result.Provenance.Should().ContainSingle().Which.LicenseFlag.Should().Be(LicenseType.ResearchOnly);
        }
    }

    // ---- AC-F1′: N co-dated factors → N rows, each under its own (…, FactorName) key --------------

    [Fact]
    public async Task Unicode_factor_tail_survives_live_and_cache_reads()
    {
        var nodes = new (string, decimal)[] { ("SMB", 0.1m), ("\uFFFFA", 0.2m), ("\U0001F600", 0.3m) };
        var adapter = new FactorAdapter(nodes);
        using var sp = BuildKernel(
            services => services.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(adapter),
            options =>
            {
                options.UseTimeSeriesStore = true;
                options.L1CacheTtl = TimeSpan.Zero;
            });
        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var expected = nodes.Select(node => new FactorObservation(s_asOf, node.Item1, node.Item2));

        var live = await pipeline.ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(s_asOf));
        live.Payload.Should().BeEquivalentTo(expected);
        var cached = await pipeline.ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(s_asOf));
        cached.Payload.Should().BeEquivalentTo(expected);
        adapter.FetchCount.Should().Be(1);
    }

    [Fact]
    public async Task AcF1Prime_Factor_gap_fill_returns_all_factors_each_under_own_key()
    {
        var nodes = new (string, decimal)[]
        {
            ("Mkt-RF", 0.0039m), ("SMB", 0.0012m), ("HML", -0.0007m), ("RF", 0.0001m),
        };
        var adapter = new FactorAdapter(nodes);
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var result = await pipeline
            .ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(s_asOf))
            .ConfigureAwait(true);

        // Every factor must survive the gap-fill append — not collapse to the last writer.
        result.Payload.Should().HaveCount(4, "each of the 4 co-dated factors is its own series");
        foreach (var (factorName, value) in nodes)
        {
            result.Payload.Should().Contain(
                f => f.FactorName == factorName && f.Value == value,
                $"factor {factorName} must survive");
        }

        // Definitive per-key probe: HML's own dataset key must hold HML's own node, proving the
        // fan-out keyed each factor separately rather than under one blanked-FactorName key.
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<FactorObservationDatasetKey, FactorObservation>()!;
        var hml = await ReadKeyAsync(store, "HML", s_asOf, s_asOf).ConfigureAwait(true);
        hml.Should().ContainSingle().Which.Value.Should()
            .Be(-0.0007m, "the (…, HML) key must hold only HML's node");
    }

    // ---- AC-F6: idempotency + DataQualityIncident on a residual collapse ------------------------

    [Fact]
    public async Task AcF6_fanout_is_idempotent_across_reruns()
    {
        var nodes = new (string, decimal)[]
        {
            ("Mkt-RF", 0.0039m), ("SMB", 0.0012m), ("HML", -0.0007m),
        };
        var adapter = new FactorAdapter(nodes);
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(adapter),
            opt =>
            {
                opt.UseTimeSeriesStore = true;
                // Force the second call to re-dispatch (re-fetch the same N rows) rather than serve
                // from L1, so the store's per-discriminant last-writer-wins dedup is exercised.
                opt.L1CacheTtl = TimeSpan.Zero;
                opt.TimeSeriesStalenessMaxAge = TimeSpan.Zero;
            });

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var first = await pipeline
            .ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(s_asOf))
            .ConfigureAwait(true);
        first.Payload.Should().HaveCount(3);

        // Re-running the fan-out over the unchanged 3-row set must leave the read at 3 rows, not 6:
        // each discriminant key dedups (…, FactorName, Date) last-writer-wins.
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<FactorObservationDatasetKey, FactorObservation>()!;

        var second = await pipeline
            .ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(s_asOf))
            .ConfigureAwait(true);
        adapter.FetchCount.Should().Be(2, "both calls must append through the adapter path");
        second.Payload.Should().HaveCount(3, "idempotent fan-out returns N rows, never 2N");

        (await ReadKeyAsync(store, "HML", s_asOf, s_asOf).ConfigureAwait(true))
            .Should().ContainSingle("HML's key holds one node after two identical fan-outs");
    }

    // ---- The persisted key must not depend on whether normalization ran (F-L6C1-01) --------------

    [Fact]
    public async Task Turning_normalizers_on_over_a_populated_store_does_not_fork_the_factor_series()
    {
        // The feed emits a non-canonical factor name. FactorName is BOTH a record body column and
        // the fan-out's dataset-key discriminant, and FactorObservationNormalizer rewrites it — so
        // before the fix the raw era persisted key "mkt-rf" and the normalized era persisted key
        // "Mkt-RF" for one logical factor. Both keys sit inside the prefix scan and both satisfy
        // KeyMatchesRequest (which deliberately does not filter the discriminant), so the read
        // silently merges two parallel series at two unit scales, and the raw-era key is never
        // re-fetched. The projected key must therefore be invariant under normalization.
        var later = s_asOf.AddDays(1);

        // Era 1 — RegisterDefaultNormalizers off (the shipped default): raw rows, raw name.
        var rawEraAdapter = new FactorAdapter([("mkt-rf", 0.39m)]);
        using (var rawEra = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(rawEraAdapter),
            opt => opt.UseTimeSeriesStore = true))
        {
            await rawEra.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(s_asOf))
                .ConfigureAwait(true);
        }

        // Era 2 — same segment folder, normalizers now ON, one new date to gap-fill.
        var normalizedEraAdapter = new FactorAdapter([("mkt-rf", 0.39m)]);
        using var normalizedEra = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(normalizedEraAdapter),
            opt =>
            {
                opt.UseTimeSeriesStore = true;
                opt.RegisterDefaultNormalizers = true;
            });

        await normalizedEra.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(later))
            .ConfigureAwait(true);

        var store = normalizedEra.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<FactorObservationDatasetKey, FactorObservation>()!;

        (await ReadKeyAsync(store, "Mkt-RF", s_asOf, later).ConfigureAwait(true))
            .Should().HaveCount(2,
                "both eras of one logical factor must land under one canonical dataset key");
        (await ReadKeyAsync(store, "mkt-rf", s_asOf, later).ConfigureAwait(true))
            .Should().BeEmpty(
                "the raw-era spelling must not be a second, permanently-stale parallel series");
    }

    // ---- Upgrade safety: a store written by a pre-canonicalisation build (F-L6C1-01 R2) ----------

    [Fact]
    public async Task A_store_written_under_the_pre_canonicalisation_key_encoding_fails_fast()
    {
        // A build BEFORE the discriminant canonicalisation projected the key column straight off the
        // record body, so an operator-named factor archive whose CSV header is a non-canonical
        // spelling ("ST_Rev" — Ken French's short-term-reversal file) persisted under the RAW
        // spelling. This build projects "ST_REV". Left undetected the upgrade alone forks one
        // logical factor across two persisted series: the legacy key is never re-fetched, the
        // canonical key reports the whole history as a gap and re-fetches it, and the
        // discriminant-blind cross-key read then returns every date TWICE. The legacy encoding must
        // therefore be detected on the read path and reported with the clear-and-re-ingest remedy,
        // before any row is appended under the new encoding.
        var later = s_asOf.AddDays(1);

        // Era 1 — a legacy directory: rows written under the raw discriminant, exactly as the
        // pre-canonicalisation Project produced them for this feed.
        using (var legacyEra = BuildKernel(_ => { }, opt => opt.UseTimeSeriesStore = true))
        {
            var legacyStore = legacyEra.GetRequiredService<ITimeSeriesStoreRegistry>()
                .Resolve<FactorObservationDatasetKey, FactorObservation>()!;
            await legacyStore
                .AppendAsync(Key("ST_Rev"), [new FactorObservation(s_asOf, "ST_Rev", 0.0012m)])
                .ConfigureAwait(true);
        }

        // Era 2 — same segment folder, current build, one new date that would gap-fill.
        var adapter = new FactorAdapter([("ST_Rev", 0.0012m)]);
        using var currentEra = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var act = async () => await currentEra.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(later))
            .ConfigureAwait(true);

        var thrown = await act.Should().ThrowAsync<MarketDataStorageException>(
            "a stored key in the pre-canonicalisation encoding must be reported, never silently "
            + "re-keyed into a second parallel series")
            .ConfigureAwait(true);

        thrown.Which.Message.Should().Contain("ST_Rev", "the message must name the stored key");
        thrown.Which.Message.Should().Contain("ST_REV", "and the encoding this build would write");
        thrown.Which.Message.Should().Contain(
            "time-series-cache.md", "and point at the delete-and-re-ingest migration section");

        // Nothing was appended under the new encoding — the guard fires before the gap fill.
        var store = currentEra.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<FactorObservationDatasetKey, FactorObservation>()!;
        (await ReadKeyAsync(store, "ST_REV", s_asOf, later).ConfigureAwait(true))
            .Should().BeEmpty("the fail-fast must precede any re-keyed append");
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
        // Two rows share (FactorName=HML, Date) — a residual per-discriminant collision. The fan-out
        // must keep one (last-writer-wins) AND emit a DataQualityIncident naming the collapsed key.
        var adapter = new FactorAdapter(
        [
            ("Mkt-RF", 0.0039m), ("HML", -0.0007m), ("HML", -0.0008m),
        ]);
        var sink = new CapturingIncidentSink();
        using var sp = BuildKernel(
            svc =>
            {
                svc.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(adapter);
                svc.AddSingleton<IDataQualityIncidentSink>(sink);
            },
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var result = await pipeline
            .ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(s_asOf))
            .ConfigureAwait(true);

        result.Payload.Should().HaveCount(2, "the duplicate HML row collapses last-writer-wins");
        result.Payload.Should().ContainSingle(row => row.FactorName == "HML")
            .Which.Value.Should().Be(-0.0008m, "the last duplicate value must survive");
        sink.Incidents.Should().ContainSingle("a residual collapse must be observable")
            .Which.Detail.Should().Contain("HML");
    }

    [Fact]
    public async Task Configured_option_sink_receives_seeded_collapse_incident()
    {
        var adapter = new FactorAdapter(
        [
            ("Mkt-RF", 0.0039m), ("HML", -0.0007m), ("HML", -0.0008m),
        ]);
        var sink = new CapturingIncidentSink();
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(adapter),
            opt =>
            {
                opt.UseTimeSeriesStore = true;
                opt.DataQualityIncidentSink = sink;
            });

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        _ = await pipeline
            .ExecuteAsync<FactorSeriesRequest, FactorObservation>(FactorRequest(s_asOf))
            .ConfigureAwait(true);

        sink.Incidents.Should().ContainSingle("the configured option is the production sink")
            .Which.Detail.Should().Contain("HML");
    }
}
