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

using System.Buffers.Binary;

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

using Boutquin.Storage.Domain.Exceptions;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// M-8 Storage-2.0-internal band (spec §5, AC-11 / AC-13 / AC-21): wire-format version fail-fast.
/// A columnar segment whose header declares a format version higher than the reader supports must
/// surface <see cref="UnsupportedFormatVersionException"/> UNWRAPPED at the <c>ExecuteAsync</c> call
/// site, enriched (per §2.3 / the spec ACs) with the record-type name and a reference to
/// <c>docs/time-series-cache.md#storage-format-versioning</c>. The pipeline must not silently swallow
/// it. A directory holding a MIX of valid + one bad segment reads the good records and throws on the
/// bad one (AC-13).
/// </summary>
public sealed class FormatVersionFailFastTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-fmtversion-{Guid.NewGuid():N}");

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

    private const string DocsAnchor = "docs/time-series-cache.md#storage-format-versioning";

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

    private static Bar Bar(DateOnly date, decimal close)
        => new(date, close, close, close, close, close, 1_000);

    private static PriceBarDatasetKey KeyFor(string ticker)
        => new("default", DataFrequency.Daily, ticker, null);

    private static PriceHistoryRequest PriceRequest(string ticker, DateOnly from, DateOnly to)
        => new([new Symbol(ticker)], new DateRange(from, to));

    private static List<Bar> Batch(DateOnly start, int count)
    {
        var seed = new List<Bar>();
        var d = start;
        for (var i = 0; i < count; i++)
        {
            seed.Add(Bar(d, 100m + i));
            d = d.AddDays(1);
        }

        return seed;
    }

    private static readonly ProviderCode s_provider = new("tiingo");

    private sealed class ThrowingBarAdapter : IDataSourceAdapter<PriceHistoryRequest, Bar>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey => s_provider;
        public int Priority => 100;
        public bool CanHandle(PriceHistoryRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(
            PriceHistoryRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("adapter must not be dispatched — the read fails first");
    }

    // Corrupt the format-version uint32 (bytes 4..7, immediately after the 4-byte header magic) of a
    // .bsct columnar segment so it declares a version higher than the reader's CurrentFormatVersion.
    private static void BumpFormatVersion(string bsctPath, uint bogusVersion)
    {
        using var fs = new FileStream(bsctPath, FileMode.Open, FileAccess.ReadWrite);
        Span<byte> versionBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(versionBytes, bogusVersion);
        fs.Seek(4, SeekOrigin.Begin);
        fs.Write(versionBytes);
    }

    // AC-11 / AC-21 · Wire-format version fail-fast with enriched, unwrapped exception.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AC11_higher_format_version_surfaces_unwrapped_enriched_exception(bool batch)
    {
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(new ThrowingBarAdapter()),
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var engine = registry.ResolveEngine<PriceBarDatasetKey, Bar>();

        await store.AppendAsync(KeyFor("AGG"), Batch(new DateOnly(2022, 1, 1), 300)).ConfigureAwait(true);
        await engine.FlushAsync().ConfigureAwait(true);

        var tsDir = Path.Combine(_tempDir, "timeseries");
        var bsct = Directory.GetFiles(tsDir, "*.bsct", SearchOption.AllDirectories).Single();
        BumpFormatVersion(bsct, bogusVersion: 999u);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = PriceRequest("AGG", new DateOnly(2022, 1, 1), new DateOnly(2022, 6, 1));
        if (batch)
        {
            request = request with { Symbols = [new Symbol("AGG"), new Symbol("BND")] };
        }

        var ex = await FluentActions
            .Invoking(() => pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request))
            .Should()
            .ThrowAsync<UnsupportedFormatVersionException>("the reader rejects the higher format version, unwrapped")
            .ConfigureAwait(true);

        ex.Which.ObservedVersion.Should().Be(999u, "the exception carries the observed version");
        ex.Which.Message.Should().Contain(nameof(Bar), "the enriched message names the record type");
        ex.Which.Message.Should().Contain(DocsAnchor, "the enriched message references the versioning docs anchor");
    }

    // AC-13 · Dirty-directory observability — a store directory holding a MIX of valid 2.0 segments
    // (one record type's engine) and a stray bad-version segment (another record type's engine). The
    // read that reaches the bad bytes fails fast, UNWRAPPED and enriched — NOT swallowed — while the
    // separate clean dataset keeps reading successfully.
    //
    // Boundary note (verified against the pinned pack): the columnar LSM range read k-way-merges
    // across every segment overlapping the key range, parsing each segment footer up front. A
    // bad-version segment in the SAME engine therefore fails fast on ANY read that consults it, not
    // as a "read good rows, then throw" partial scan. The honest dirty-mix contract at the pipeline
    // layer is: the affected engine's read surfaces the exception unwrapped; an UNAFFECTED engine's
    // read still succeeds — that is the "mix" the pipeline must observe without swallowing.
    [Fact]
    public async Task AC13_dirty_directory_mix_unaffected_dataset_reads_affected_throws_unwrapped()
    {
        using var sp = BuildKernel(
            svc =>
            {
                svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(new ThrowingBarAdapter());
                svc.AddSingleton<IDataSourceAdapter<EconomicSeriesRequest, ScalarObservation>>(
                    new ThrowingScalarAdapter());
            },
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();

        // Bar store (will be corrupted) — its own engine + segment set.
        var barStore = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var barEngine = registry.ResolveEngine<PriceBarDatasetKey, Bar>();
        await barStore.AppendAsync(KeyFor("AGG"), Batch(new DateOnly(2022, 1, 1), 300)).ConfigureAwait(true);
        await barEngine.FlushAsync().ConfigureAwait(true);

        // ScalarObservation store (stays clean) — a DIFFERENT engine writing to a different segment dir.
        var scalarStore = registry.Resolve<ScalarObservationDatasetKey, ScalarObservation>()!;
        var scalarEngine = registry.ResolveEngine<ScalarObservationDatasetKey, ScalarObservation>();
        var scalarKey = new ScalarObservationDatasetKey("default", "GDP");
        var scalars = new List<ScalarObservation>();
        var sd = new DateOnly(2022, 1, 1);
        for (var i = 0; i < 100; i++)
        {
            scalars.Add(new ScalarObservation(sd, 1.0m + i, "index"));
            sd = sd.AddDays(1);
        }

        await scalarStore.AppendAsync(scalarKey, scalars).ConfigureAwait(true);
        await scalarEngine.FlushAsync().ConfigureAwait(true);

        // Corrupt ONLY the Bar record type's segment directory.
        var barDir = Path.Combine(_tempDir, "timeseries", nameof(Bar));
        var barSegment = Directory.GetFiles(barDir, "*.bsct", SearchOption.AllDirectories).First();
        BumpFormatVersion(barSegment, bogusVersion: 999u);

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        // The UNAFFECTED ScalarObservation dataset reads successfully — its segments are clean.
        var goodResult = await pipeline.ExecuteAsync<EconomicSeriesRequest, ScalarObservation>(
            new EconomicSeriesRequest(
                new EconomicSeriesId("GDP"),
                new DateRange(new DateOnly(2022, 1, 1), new DateOnly(2022, 2, 1))))
            .ConfigureAwait(true);
        goodResult.Payload.Should().NotBeEmpty("the clean, unaffected dataset keeps reading");

        // The AFFECTED Bar dataset fails fast — unwrapped and enriched, never swallowed.
        var ex = await FluentActions
            .Invoking(() => pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
                PriceRequest("AGG", new DateOnly(2022, 1, 1), new DateOnly(2022, 6, 1))))
            .Should()
            .ThrowAsync<UnsupportedFormatVersionException>("the read reaching the bad-version bytes throws")
            .ConfigureAwait(true);
        ex.Which.Message.Should().Contain(nameof(Bar), "the enriched message names the affected record type");
        ex.Which.Message.Should().Contain(DocsAnchor, "the enriched message references the versioning docs anchor");
    }

    private sealed class ThrowingScalarAdapter
        : IDataSourceAdapter<EconomicSeriesRequest, ScalarObservation>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey => new("fred");
        public int Priority => 100;
        public bool CanHandle(EconomicSeriesRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<ScalarObservation>>> FetchAsync(
            EconomicSeriesRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("adapter must not be dispatched — clean read is fully subsumed");
    }
}
