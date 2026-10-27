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
using Boutquin.MarketData.Abstractions.Enums;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.Storage.Domain.Enums;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;
using Boutquin.Storage.Infrastructure.LsmTree;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// M-8 Storage-2.0-internal band (spec §5, AC-19): compaction-strategy selection and the
/// <c>ExcludeAndSurface</c> self-healing corrupt-segment recovery MarketData opts into (§2.1 / Q12).
/// The strategy halves assert the builder resolves each <see cref="TimeSeriesCompactionStrategy"/> to
/// its concrete Storage strategy type; the recovery halves bit-flip a manifest-listed segment and
/// prove <c>ExcludeAndSurface</c> re-opens with shrunk coverage + a fired callback while
/// <c>FailClosed</c> throws.
/// </summary>
public sealed class CompactionAndRecoveryTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-compaction-{Guid.NewGuid():N}");

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

    private static Bar Bar(DateOnly date, decimal close)
        => new(date, close, close, close, close, close, 1_000);

    private static PriceBarDatasetKey KeyFor(string ticker)
        => new("default", DataFrequency.Daily, ticker, null);

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

    private TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar> Builder(string subdir)
        => new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>(nameof(Bar))
            .WithSegmentRoot(Path.Combine(_tempDir, subdir, "{record-type}"));

    // AC-19 (strategy half) · The builder resolves each TimeSeriesCompactionStrategy to its concrete
    // Storage strategy type (SizeTiered default already locked in M-4′ registration tests; this adds
    // the Leveled + Full switch assertions).
    [Fact]
    public void AC19_compaction_strategy_resolves_to_concrete_type()
    {
        Builder("st-default").ResolveCompactionStrategy()
            .Should().BeOfType<SizeTieredCompactionStrategy<TimeSeriesKey<PriceBarDatasetKey>>>(
                "the default is SizeTiered");

        Builder("st-leveled").WithCompactionStrategy(TimeSeriesCompactionStrategy.Leveled).ResolveCompactionStrategy()
            .Should().BeOfType<LeveledCompactionStrategy<TimeSeriesKey<PriceBarDatasetKey>>>(
                "Leveled resolves to LeveledCompactionStrategy");

        Builder("st-full").WithCompactionStrategy(TimeSeriesCompactionStrategy.Full).ResolveCompactionStrategy()
            .Should().BeOfType<FullCompactionStrategy<TimeSeriesKey<PriceBarDatasetKey>>>(
                "Full resolves to FullCompactionStrategy");
    }

    // AC-19 (default disposition) · The builder defaults to ExcludeAndSurface — the cache-tier
    // self-heal — rather than Storage's authoritative FailClosed default.
    [Fact]
    public void AC19_default_corrupt_segment_recovery_is_exclude_and_surface()
        => Builder("disp").CorruptSegmentRecovery.Should().Be(CorruptSegmentRecoveryDisposition.ExcludeAndSurface);

    // AC-19 (ExcludeAndSurface self-heal) · A bit-flipped manifest-listed segment causes engine open
    // to SUCCEED with shrunk coverage rather than throw; OnCorruptSegmentExcluded fires naming the
    // excluded segment.
    [Fact]
    public async Task AC19_exclude_and_surface_reopen_succeeds_and_fires_callback()
    {
        var subdir = "exclude";
        var key = KeyFor("AGG");

        // Build → append → flush (writes a segment + manifest) → dispose.
        var segmentFolder = await BuildFlushDisposeAsync(subdir, key).ConfigureAwait(true);

        // Corrupt the (single) flushed segment so its file CRC no longer matches the manifest entry.
        var segment = Directory.GetFiles(segmentFolder, "*.bsct", SearchOption.TopDirectoryOnly).Single();
        FlipOneByte(segment);

        // Re-open the SAME store under ExcludeAndSurface with a callback; open must SUCCEED and the
        // callback must fire naming the corrupt segment.
        var excluded = new List<string>();
        var stack = await Builder(subdir)
            .WithCorruptSegmentRecovery(
                CorruptSegmentRecoveryDisposition.ExcludeAndSurface,
                ex => excluded.Add(ex.FileName))
            .BuildStackAsync()
            .ConfigureAwait(true);

        excluded.Should().ContainSingle("the one corrupt segment is excluded")
            .Which.Should().Be(Path.GetFileName(segment), "the callback names the excluded segment");

        // Coverage shrank: the excluded segment's records are no longer served (the store re-opened
        // with an empty segment set for that key).
        var coverage = await stack.Store.GetCoverageAsync(key).ConfigureAwait(true);
        coverage.Should().BeNull("the excluded segment's coverage is gone after self-heal");

        await ((IAsyncDisposable)stack.Engine).DisposeAsync().ConfigureAwait(true);
    }

    // AC-19 (self-heal re-fetch through gap fill · F-L4-1) · The ExcludeAndSurface half above stops at
    // "coverage shrank". Per spec §5 AC-19 + §2.1/Q12, the self-heal is not just "open succeeds" — a
    // SUBSEQUENT read over the excluded span must re-dispatch to an adapter via the DataPipeline
    // gap-fill flow and repopulate the missing span. This test re-opens the SAME corrupted segment
    // root through the PRODUCTION composition root (AddMarketDataKernel, which resolves
    // CorruptSegmentRecovery to its ExcludeAndSurface default — see the sibling default-disposition
    // test above) and drives a real DataPipeline.ExecuteAsync over the excluded range.
    [Fact]
    public async Task AC19_self_heal_rereads_excluded_span_via_gap_fill()
    {
        var subdir = "self-heal";
        var key = KeyFor("AGG");
        var affectedFrom = new DateOnly(2022, 1, 1);
        var affectedTo = affectedFrom.AddDays(299); // matches Batch(start, 300) below.

        // Build → append → flush → dispose → corrupt, exactly like the ExcludeAndSurface reopen test.
        var segmentFolder = await BuildFlushDisposeAsync(subdir, key).ConfigureAwait(true);
        var segment = Directory.GetFiles(segmentFolder, "*.bsct", SearchOption.TopDirectoryOnly).Single();
        FlipOneByte(segment);

        // Re-open the SAME segment root through AddMarketDataKernel — the real composition root the
        // production DataPipeline runs behind. Its store builder never overrides CorruptSegmentRecovery
        // (ApplyStoreShapingOptions), so it inherits the builder's ExcludeAndSurface default: open
        // succeeds with the corrupt segment's coverage excluded, same as the direct-builder reopen.
        var adapter = new SelfHealBarAdapter();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_tempDir, "snapshots"),
            rawDocumentDirectory: Path.Combine(_tempDir, "raw"),
            checkpointDirectory: Path.Combine(_tempDir, "checkpoints"),
            eventLogDirectory: Path.Combine(_tempDir, "events"),
            timeSeriesDirectory: Path.Combine(_tempDir, subdir),
            configurePipeline: opt => opt.UseTimeSeriesStore = true);
        services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter);
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;

        // Confirms the exclusion already happened at open (mirrors the sibling ExcludeAndSurface test):
        // the corrupt segment's coverage is gone before any pipeline read is issued.
        (await store.GetCoverageAsync(key).ConfigureAwait(true))
            .Should().BeNull("the corrupt segment was excluded at open, same as the direct-builder reopen test");

        // The re-fetch half: ExecuteAsync over the now-uncovered range must dispatch the adapter via
        // gap fill and repopulate the missing span — not return stale/empty data.
        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = new PriceHistoryRequest([new Symbol("AGG")], new DateRange(affectedFrom, affectedTo));
        var result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request).ConfigureAwait(true);

        adapter.Dispatches.Should().ContainSingle(
            "zero coverage over the excluded span makes the whole request one gap, re-dispatched to the adapter");
        adapter.Dispatches[0].From.Should().Be(affectedFrom);
        adapter.Dispatches[0].To.Should().Be(affectedTo);
        result.Provenance.Should().OnlyContain(
            p => p.RetrievalMode == RetrievalMode.Live,
            "the whole requested range fell in the gap and was freshly fetched — the self-heal re-fetch");
        var expected = Enumerable.Range(0, 300)
            .Select(offset => Bar(affectedFrom.AddDays(offset), 100m)).ToArray();
        result.Payload.Should().Equal(expected, "gap fill must restore all 300 dated records and their values");

        // Follow-up read: the excluded span is now durably present in the store (self-healed).
        var healed = new List<Bar>();
        await foreach (var b in store.GetRangeAsync(key, affectedFrom, affectedTo).ConfigureAwait(true))
        {
            healed.Add(b);
        }

        healed.Should().Equal(expected,
            "a follow-up read must retain the entire repaired span with every value intact");
    }

    [Fact]
    public async Task Pipeline_ingested_rows_are_refetched_after_corrupt_segment_exclusion()
    {
        ServiceProvider Host(SelfHealBarAdapter adapter)
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddMarketDataKernel(
                Path.Combine(_tempDir, "snapshots"), Path.Combine(_tempDir, "raw"),
                Path.Combine(_tempDir, "checkpoints"), Path.Combine(_tempDir, "events"),
                timeSeriesDirectory: Path.Combine(_tempDir, "ingested"),
                configurePipeline: options =>
                {
                    options.UseTimeSeriesStore = true;
                    options.L1CacheTtl = TimeSpan.Zero;
                    options.TimeSeriesStalenessMaxAge = TimeSpan.MaxValue;
                });
            services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter);
            return services.BuildServiceProvider();
        }

        var from = new DateOnly(2022, 1, 1);
        var request = new PriceHistoryRequest([new Symbol("AGG")], new DateRange(from, from.AddDays(299)));
        using (var writer = Host(new SelfHealBarAdapter()))
        {
            var written = await writer.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<PriceHistoryRequest, Bar>(request).ConfigureAwait(true);
            written.Payload.Should().HaveCount(300);
            await writer.GetRequiredService<ITimeSeriesStoreRegistry>().ResolveEngine<PriceBarDatasetKey, Bar>()
                .FlushAsync().ConfigureAwait(true);
        }

        var segment = Directory.GetFiles(Path.Combine(_tempDir, "ingested"), "*.bsct", SearchOption.AllDirectories).Single();
        FlipOneByte(segment);
        var repair = new SelfHealBarAdapter();
        using var reader = Host(repair);
        var recovered = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<PriceHistoryRequest, Bar>(request).ConfigureAwait(true);
        repair.Dispatches.Should().ContainSingle("acknowledged row dates are not source-confirmed empty dates");
        recovered.Payload.Should().HaveCount(300);
    }

    // AC-19 (FailClosed) · The parallel FailClosed disposition throws an InvalidDataException naming
    // the corrupt segment at open — MarketData deliberately overrides Storage's default to the
    // self-healing ExcludeAndSurface, but the FailClosed authoritative-store path is available.
    [Fact]
    public async Task AC19_fail_closed_reopen_throws_naming_the_segment()
    {
        var subdir = "failclosed";
        var key = KeyFor("AGG");

        var segmentFolder = await BuildFlushDisposeAsync(subdir, key).ConfigureAwait(true);
        var segment = Directory.GetFiles(segmentFolder, "*.bsct", SearchOption.TopDirectoryOnly).Single();
        FlipOneByte(segment);

        var act = () => Builder(subdir)
            .WithCorruptSegmentRecovery(CorruptSegmentRecoveryDisposition.FailClosed)
            .BuildStackAsync();

        (await act.Should().ThrowAsync<InvalidDataException>("FailClosed rejects a corrupt manifest-listed segment")
            .ConfigureAwait(true))
            .Which.Message.Should().Contain(Path.GetFileName(segment), "the throw names the corrupt segment");
    }

    // Build a store in `subdir`, append + flush a batch under `key`, dispose the engine (closing the
    // segment file handles so it can be corrupted on disk), and return the segment folder.
    private async Task<string> BuildFlushDisposeAsync(string subdir, PriceBarDatasetKey key)
    {
        var stack = await Builder(subdir).BuildStackAsync().ConfigureAwait(true);
        await stack.Store.AppendAsync(key, Batch(new DateOnly(2022, 1, 1), 300)).ConfigureAwait(true);
        await stack.Engine.FlushAsync().ConfigureAwait(true);
        var segmentFolder = Path.Combine(_tempDir, subdir, nameof(Bar));
        await ((IAsyncDisposable)stack.Engine).DisposeAsync().ConfigureAwait(true);
        return segmentFolder;
    }

    // Flip one byte in the middle of the segment body without changing the file size — a CRC mismatch
    // that trips the manifest integrity check at open (size stays equal so only the CRC diverges).
    private static void FlipOneByte(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        // Land in the body, well past the header, well before the trailer.
        var offset = fs.Length / 2;
        fs.Seek(offset, SeekOrigin.Begin);
        var b = fs.ReadByte();
        fs.Seek(offset, SeekOrigin.Begin);
        fs.WriteByte((byte)(b ^ 0xFF));
    }

    // A single-purpose Bar adapter for the self-heal re-fetch test: returns one record per requested
    // day (so the gap-fill append has something durable to write back) and records every dispatched
    // range (so the test can assert the excluded span was actually re-fetched, not served stale/empty).
    private sealed class SelfHealBarAdapter : IDataSourceAdapter<PriceHistoryRequest, Bar>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new("tiingo");

        public int Priority => 100;

        public List<DateRange> Dispatches { get; } = [];

        public bool CanHandle(PriceHistoryRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(
            PriceHistoryRequest request, CancellationToken cancellationToken = default)
        {
            Dispatches.Add(request.Range);
            var recs = new List<Bar>();
            for (var d = request.Range.From; d <= request.Range.To; d = d.AddDays(1))
            {
                recs.Add(Bar(d, 100m));
            }

            var coverage = new DataCoverage(recs.Count, recs.Count, 0, recs.Count > 0 ? 1.0m : 0.0m);
            var provenance = new DataProvenance(
                ProviderKey, request.DatasetKey, LicenseType.Free, RetrievalMode.Api,
                FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, $"https://tiingo.test/{request.DatasetKey}");
            return Task.FromResult(new DataEnvelope<IReadOnlyList<Bar>>(recs, coverage, [], [provenance]));
        }
    }
}
