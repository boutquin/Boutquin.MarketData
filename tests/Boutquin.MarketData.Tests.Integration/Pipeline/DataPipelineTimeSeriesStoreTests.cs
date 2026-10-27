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
using Boutquin.MarketData.Abstractions.Enums;
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

using Boutquin.Storage.Domain.Exceptions;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// M-7 integration tests (§2.3, AC-1..AC-22): drive the rewritten <c>DataPipeline.ExecuteAsync</c>
/// gap-fill flow through the REAL <see cref="ServiceCollectionExtensions.AddMarketDataKernel"/>
/// composition root — resolving a live <c>LsmTimeSeriesStore</c> from the registry, exercising
/// coverage/gap detection, adapter dispatch for gaps only, and merged range reads with correct
/// provenance. The flag-ON path is proven non-inert by round-tripping through the production store.
/// </summary>
public sealed class DataPipelineTimeSeriesStoreTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-tsstore-{Guid.NewGuid():N}");

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

    private static PriceHistoryRequest PriceRequest(string ticker, DateOnly from, DateOnly to)
        => new([new Symbol(ticker)], new DateRange(from, to));

    private static Bar Bar(DateOnly date, decimal close)
        => new(date, close, close, close, close, close, 1_000);

    private static PriceBarDatasetKey KeyFor(string ticker)
        => new("default", DataFrequency.Daily, ticker, null);

    private static DataProvenance ApiProvenance(string provider, string dataset)
        => new(new ProviderCode(provider), dataset, LicenseType.Free, RetrievalMode.Api,
            FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, $"https://{provider}.test/{dataset}");

    // A counting Bar adapter that returns records for exactly the requested range each call and
    // records the (from, to) of every dispatch — the anti-inertness proof needs to observe that
    // the flag-ON path dispatches for GAPS ONLY (never the full request when the store subsumes).
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

    private sealed class CapturingIncidentSink : IDataQualityIncidentSink
    {
        public List<DataQualityIncident> Incidents { get; } = [];

        public void Report(DataQualityIncident incident) => Incidents.Add(incident);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task FailQuery_from_kernel_options_rejects_a_real_corrupted_columnar_row_group(
        bool batch, bool continueOnFailure)
    {
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 1, 4);
        var adapter = new CountingBarAdapter("default", _ => []);
        var sink = new CapturingIncidentSink();
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            options =>
            {
                options.UseTimeSeriesStore = true;
                options.QuarantineBehavior = QuarantineBehavior.FailQuery;
                options.ContinueOnPartitionFailure = continueOnFailure;
                options.DataQualityIncidentSink = sink;
            });

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        await store.AppendAsync(KeyFor("SPY"),
        [
            Bar(from, 101m),
            Bar(from.AddDays(1), 102m),
            Bar(from.AddDays(2), 103m),
            Bar(to, 104m),
        ]).ConfigureAwait(true);
        await registry.ResolveEngine<PriceBarDatasetKey, Bar>().FlushAsync().ConfigureAwait(true);
        await CorruptFirstRowGroupAsync().ConfigureAwait(true);

        var request = batch
            ? new PriceHistoryRequest([new Symbol("SPY"), new Symbol("QQQ")], new DateRange(from, to))
            : PriceRequest("SPY", from, to);
        var act = async () => await sp.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<PriceHistoryRequest, Bar>(request)
            .ConfigureAwait(true);

        await act.Should().ThrowAsync<CorruptSegmentException>(
            "the non-default FailQuery option must reach the real columnar reader")
            .ConfigureAwait(true);
        adapter.Dispatches.Should().BeEmpty("FailQuery rejects corrupt storage before live recovery dispatch");
        sink.Incidents.Should().ContainSingle()
            .Which.Kind.Should().Be(DataQualityIncidentKind.RowGroupQuarantine);
    }

    [Fact]
    public async Task SkipRowGroup_from_kernel_options_returns_authenticated_surrounding_rows_without_refetch()
    {
        var from = new DateOnly(2024, 1, 1);
        const int recordCount = 367;
        var to = from.AddDays(recordCount - 1);
        var adapter = new CountingBarAdapter("default", _ => []);
        var sink = new CapturingIncidentSink();
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            options =>
            {
                options.UseTimeSeriesStore = true;
                options.QuarantineBehavior = QuarantineBehavior.SkipRowGroup;
                options.DataQualityIncidentSink = sink;
            });

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var records = Enumerable.Range(0, recordCount)
            .Select(offset => Bar(from.AddDays(offset), 100m + offset))
            .ToList();
        await store.AppendAsync(KeyFor("SPY"), records).ConfigureAwait(true);
        await registry.ResolveEngine<PriceBarDatasetKey, Bar>().FlushAsync().ConfigureAwait(true);
        await CorruptFirstRowGroupAsync(minimumRowGroupCount: 2).ConfigureAwait(true);

        var result = await sp.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<PriceHistoryRequest, Bar>(PriceRequest("SPY", from, to))
            .ConfigureAwait(true);

        adapter.Dispatches.Should().BeEmpty("SkipRowGroup never schedules a recovery fetch");
        result.Payload.Should().NotBeEmpty("an intact later row group surrounds the corrupt group");
        result.Payload.Should().HaveCountLessThan(recordCount);
        result.Payload.Should().NotContain(row => row.Date == from,
            "records from the corrupted first row group must not escape authentication");
        sink.Incidents.Should().ContainSingle()
            .Which.Kind.Should().Be(DataQualityIncidentKind.RowGroupQuarantine);
    }

    [Fact]
    public async Task TreatAsGap_refetches_a_real_corrupted_columnar_row_group()
    {
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 1, 4);
        var adapter = new CountingBarAdapter("default", range =>
        {
            range.Should().Be(new DateRange(from, to), "quarantine recovery must stay request-bounded");
            return Enumerable.Range(0, range.To.DayNumber - range.From.DayNumber + 1)
                .Select(offset => Bar(range.From.AddDays(offset), 999m))
                .ToList();
        });
        var sink = new CapturingIncidentSink();
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            options =>
            {
                options.UseTimeSeriesStore = true;
                options.QuarantineBehavior = QuarantineBehavior.TreatAsGap;
                options.DataQualityIncidentSink = sink;
            });

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        await store.AppendAsync(KeyFor("SPY"),
        [
            Bar(from, 101m),
            Bar(from.AddDays(1), 102m),
            Bar(from.AddDays(2), 103m),
            Bar(to, 104m),
        ]).ConfigureAwait(true);
        await registry.ResolveEngine<PriceBarDatasetKey, Bar>().FlushAsync().ConfigureAwait(true);
        await CorruptFirstRowGroupAsync().ConfigureAwait(true);

        var result = await sp.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<PriceHistoryRequest, Bar>(PriceRequest("SPY", from, to))
            .ConfigureAwait(true);

        adapter.Dispatches.Should().Equal(new DateRange(from, to));
        result.Payload.Should().HaveCount(4).And.OnlyContain(bar => bar.Close == 999m);
        result.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Partial);
        result.Issues.Should().Contain(issue =>
            issue.Code.Value == "PARTITION_FAILED"
            && issue.Message.Contains("quarantined row group", StringComparison.OrdinalIgnoreCase));
        sink.Incidents.Should().ContainSingle()
            .Which.Kind.Should().Be(DataQualityIncidentKind.RowGroupQuarantine);
    }

    [Fact]
    public async Task TreatAsGap_failed_bounded_refetch_retries_on_the_next_call()
    {
        var from = new DateOnly(2024, 2, 1);
        var to = new DateOnly(2024, 2, 4);
        var attempt = 0;
        var adapter = new CountingBarAdapter("default", range =>
        {
            range.Should().Be(new DateRange(from, to), "every retry must stay request-bounded");
            if (Interlocked.Increment(ref attempt) == 1)
            {
                throw new IOException("seeded first recovery failure");
            }

            return Enumerable.Range(0, range.To.DayNumber - range.From.DayNumber + 1)
                .Select(offset => Bar(range.From.AddDays(offset), 777m))
                .ToList();
        });
        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            options =>
            {
                options.UseTimeSeriesStore = true;
                options.QuarantineBehavior = QuarantineBehavior.TreatAsGap;
            });

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        await store.AppendAsync(KeyFor("SPY"),
        [
            Bar(from, 101m),
            Bar(from.AddDays(1), 102m),
            Bar(from.AddDays(2), 103m),
            Bar(to, 104m),
        ]).ConfigureAwait(true);
        await registry.ResolveEngine<PriceBarDatasetKey, Bar>().FlushAsync().ConfigureAwait(true);
        await CorruptFirstRowGroupAsync().ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = PriceRequest("SPY", from, to);
        var first = async () => await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request).ConfigureAwait(true);
        await first.Should().ThrowAsync<Exception>("the first recovery fetch is seeded to fail")
            .ConfigureAwait(true);

        var recovered = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request).ConfigureAwait(true);

        adapter.Dispatches.Should().Equal(new DateRange(from, to), new DateRange(from, to));
        recovered.Payload.Should().HaveCount(4).And.OnlyContain(bar => bar.Close == 777m);
        recovered.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Partial);
    }

    private async Task CorruptFirstRowGroupAsync(int minimumRowGroupCount = 1)
    {
        var path = Directory.GetFiles(Path.Combine(_tempDir, "timeseries"), "*.bsct", SearchOption.AllDirectories)
            .Should().ContainSingle().Subject;
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);

        var outerFooterLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(bytes.Length - 8, 4)));
        var innerLength = bytes.Length - 8 - outerFooterLength - 8;
        var inner = bytes.AsSpan(8, innerLength);
        var innerFooterLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            inner[^8..^4]));
        var innerFooterStart = inner.Length - 8 - 4 - innerFooterLength;
        var innerFooter = inner.Slice(innerFooterStart, innerFooterLength);

        var extensionOffset = FindRowGroupCrcExtension(innerFooter, minimumRowGroupCount);
        extensionOffset.Should().BeGreaterThanOrEqualTo(0, "Storage emits row-group CRC metadata");
        innerFooter[extensionOffset + 7] ^= 0xff;
        BinaryPrimitives.WriteUInt32LittleEndian(
            inner.Slice(innerFooterStart + innerFooterLength, 4),
            ComputeCrc32C(innerFooter));

        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(true);
    }

    private static int FindRowGroupCrcExtension(ReadOnlySpan<byte> footer, int minimumRowGroupCount)
    {
        for (var offset = 0; offset <= footer.Length - 7; offset++)
        {
            if (footer[offset] != 0x03)
            {
                continue;
            }

            var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(footer.Slice(offset + 1, 4));
            var rowGroupCount = BinaryPrimitives.ReadUInt16LittleEndian(footer.Slice(offset + 5, 2));
            if (rowGroupCount >= minimumRowGroupCount
                && payloadLength == 2u + ((uint)rowGroupCount * sizeof(uint))
                && offset + 5L + payloadLength <= footer.Length)
            {
                return offset;
            }
        }

        return -1;
    }

    private static uint ComputeCrc32C(ReadOnlySpan<byte> bytes)
    {
        const uint reflectedCastagnoliPolynomial = 0x82f63b78u;
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : reflectedCastagnoliPolynomial);
            }
        }

        return ~crc;
    }

    // AC-1 · Full subsumption — zero adapter calls.
    [Fact]
    public async Task AC1_full_subsumption_returns_from_store_without_dispatch()
    {
        var adapter = new CountingBarAdapter("tiingo", _ => throw new InvalidOperationException(
            "adapter must NOT be dispatched on a fully-subsumed request"));

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        // Pre-populate the LIVE store resolved from the production registry with coverage
        // (2000-01-01, 2026-04-18) under (default, Daily, "AGG").
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>();
        store.Should().NotBeNull("AddMarketDataKernel registers a live columnar Bar store");

        var key = KeyFor("AGG");
        // Coverage must span EXACTLY (2000-01-01 .. 2026-04-18): include both endpoints so
        // GetGapsAsync (requested.Minus(coverage)) yields no suffix gap for the request's To.
        var coverageFrom = new DateOnly(2000, 1, 1);
        var coverageTo = new DateOnly(2026, 4, 18);
        var seed = new List<Bar> { Bar(coverageFrom, 100m), Bar(coverageTo, 100m) };
        for (var d = coverageFrom.AddDays(37); d < coverageTo; d = d.AddDays(37))
        {
            seed.Add(Bar(d, 100m));
        }

        await store!.AppendAsync(key, seed).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = PriceRequest("AGG", new DateOnly(2020, 1, 1), new DateOnly(2026, 4, 18));

        var result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request)
            .ConfigureAwait(true);

        adapter.Dispatches.Should().BeEmpty("full subsumption dispatches no adapter");
        result.Payload.Should().NotBeEmpty();
        result.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Cache);
    }

    // AC-2 · Prefix gap. Store covers (2010-01-01, 2026-04-18); request (2005-01-01, 2026-04-18)
    // dispatches the adapter once for the prefix (2005-01-01, 2009-12-31), appends, returns merged.
    [Fact]
    public async Task AC2_prefix_gap_dispatches_once_and_returns_partial()
    {
        var adapter = new CountingBarAdapter("tiingo", range =>
        [
            Bar(range.From, 10m),
            Bar(range.To, 11m),
        ]);

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var key = KeyFor("AGG");
        await store.AppendAsync(key,
        [
            Bar(new DateOnly(2010, 1, 1), 100m),
            Bar(new DateOnly(2026, 4, 18), 100m),
        ]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = PriceRequest("AGG", new DateOnly(2005, 1, 1), new DateOnly(2026, 4, 18));

        var result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request)
            .ConfigureAwait(true);

        adapter.Dispatches.Should().ContainSingle("exactly one prefix gap is fetched");
        adapter.Dispatches[0].From.Should().Be(new DateOnly(2005, 1, 1));
        adapter.Dispatches[0].To.Should().Be(new DateOnly(2009, 12, 31));
        result.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Partial);
        // Merged read includes the appended prefix records + the pre-existing coverage.
        result.Payload.Should().Contain(b => b.Date == new DateOnly(2005, 1, 1));
        result.Payload.Should().Contain(b => b.Date == new DateOnly(2010, 1, 1));
    }

    // AC-4 · Two gaps. Store covers (2010, 2020); request (2005, 2026) dispatches twice —
    // (2005, 2009) and (2021, 2026). Both appended; merged returned.
    [Fact]
    public async Task AC4_two_gaps_dispatch_twice()
    {
        var adapter = new CountingBarAdapter("tiingo", range => [Bar(range.From, 5m), Bar(range.To, 6m)]);

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var key = KeyFor("AGG");
        await store.AppendAsync(key,
        [
            Bar(new DateOnly(2010, 1, 1), 100m),
            Bar(new DateOnly(2020, 12, 31), 100m),
        ]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = PriceRequest("AGG", new DateOnly(2005, 1, 1), new DateOnly(2026, 12, 31));

        var result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request)
            .ConfigureAwait(true);

        adapter.Dispatches.Should().HaveCount(2, "one prefix gap and one suffix gap");
        adapter.Dispatches.Should().Contain(r => r.From == new DateOnly(2005, 1, 1) && r.To == new DateOnly(2009, 12, 31));
        adapter.Dispatches.Should().Contain(r => r.From == new DateOnly(2021, 1, 1) && r.To == new DateOnly(2026, 12, 31));
        result.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Partial);
    }

    // AC-7 · Flag OFF = existing behavior. With UseTimeSeriesStore = false, the store path is never
    // reached; the request flows through the legacy adapter chain (Api provenance), and the
    // pre-populated store is ignored.
    [Fact]
    public async Task AC7_flag_off_uses_legacy_path()
    {
        var adapter = new CountingBarAdapter("tiingo", range => [Bar(range.From, 42m)]);

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = false);

        // Pre-populate the store with full coverage — the legacy path must NOT consult it.
        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        await store.AppendAsync(KeyFor("AGG"),
        [
            Bar(new DateOnly(2020, 1, 1), 999m),
            Bar(new DateOnly(2020, 1, 31), 999m),
        ]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = PriceRequest("AGG", new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 31));
        var result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request)
            .ConfigureAwait(true);

        // Legacy path dispatched the adapter (whole request), returning the adapter's value (42),
        // not the store's (999) — proving the store path was bypassed.
        adapter.Dispatches.Should().ContainSingle();
        result.Payload.Should().Contain(b => b.Open == 42m);
        result.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Api);
    }

    // AC-25 (behavior lands in M-7; AC id owned by M-8) · Adapter-dispatch failure during gap fill.
    // (a) First gap throws, nothing appended ⇒ ExecuteAsync rethrows the adapter exception.
    [Fact]
    public async Task AC25a_first_gap_failure_rethrows()
    {
        var adapter = new CountingBarAdapter("tiingo", _ =>
            throw new InvalidOperationException("upstream 503"));

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        await store.AppendAsync(KeyFor("AGG"),
        [
            Bar(new DateOnly(2010, 1, 1), 100m),
            Bar(new DateOnly(2020, 12, 31), 100m),
        ]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        // Prefix-only gap so the FIRST (and only) gap is the one that fails, nothing appended.
        var request = PriceRequest("AGG", new DateOnly(2005, 1, 1), new DateOnly(2020, 12, 31));

        await FluentActions
            .Invoking(() => pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*upstream 503*")
            .ConfigureAwait(true);
    }

    // AC-25 (b) · First gap succeeds and is appended, second gap throws ⇒ Partial envelope naming
    // the still-missing range; the first gap's records are durably present on a subsequent read.
    [Fact]
    public async Task AC25b_later_gap_failure_returns_partial_and_retains_first()
    {
        var callCount = 0;
        var adapter = new CountingBarAdapter("tiingo", range =>
        {
            callCount++;
            // Fail the SUFFIX gap (starts 2021); succeed the prefix (starts 2005).
            if (range.From >= new DateOnly(2021, 1, 1))
            {
                throw new InvalidOperationException("suffix upstream down");
            }

            return [Bar(range.From, 7m), Bar(range.To, 8m)];
        });

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var key = KeyFor("AGG");
        await store.AppendAsync(key,
        [
            Bar(new DateOnly(2010, 1, 1), 100m),
            Bar(new DateOnly(2020, 12, 31), 100m),
        ]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = PriceRequest("AGG", new DateOnly(2005, 1, 1), new DateOnly(2026, 12, 31));

        var result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request)
            .ConfigureAwait(true);

        result.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Partial);
        result.Issues.Should().Contain(i =>
            i.Message.Contains("2021", StringComparison.Ordinal), "the still-missing range is named");

        // The first gap's records are durably present on a subsequent read.
        var stored = new List<Bar>();
        await foreach (var b in store.GetRangeAsync(
            key, new DateOnly(2005, 1, 1), new DateOnly(2009, 12, 31)).ConfigureAwait(true))
        {
            stored.Add(b);
        }

        stored.Should().Contain(b => b.Date == new DateOnly(2005, 1, 1));
    }

    // AC-3 · Suffix gap (nightly extend-by-one-day). Store covers up to yesterday; a request whose
    // To reaches today dispatches once for the trailing gap only.
    [Fact]
    public async Task AC3_suffix_gap_extends_by_tail()
    {
        var adapter = new CountingBarAdapter("tiingo", range => [Bar(range.From, 3m), Bar(range.To, 4m)]);

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var key = KeyFor("AGG");
        await store.AppendAsync(key,
        [
            Bar(new DateOnly(2026, 1, 1), 100m),
            Bar(new DateOnly(2026, 6, 30), 100m),
        ]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = PriceRequest("AGG", new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 5));

        var result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request)
            .ConfigureAwait(true);

        adapter.Dispatches.Should().ContainSingle("only the trailing suffix is a gap");
        adapter.Dispatches[0].From.Should().Be(new DateOnly(2026, 7, 1));
        adapter.Dispatches[0].To.Should().Be(new DateOnly(2026, 7, 5));
        result.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Partial);
    }

    // AC-5 · Server-side clip is free after first fetch. Three requests with different pre-inception
    // `from` values against a ticker whose inception is 2003-09-26. First dispatches; second/third
    // hit full subsumption with zero adapter calls.
    [Fact]
    public async Task AC5_repeated_preinception_requests_subsume_after_first()
    {
        var inception = new DateOnly(2003, 9, 26);
        var adapter = new CountingBarAdapter("tiingo", range =>
        {
            // The source clips to actual inception regardless of the requested (pre-inception) from.
            var recs = new List<Bar>();
            for (var d = inception; d <= range.To; d = d.AddDays(30))
            {
                recs.Add(Bar(d, 50m));
            }

            return recs;
        });

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var to = new DateOnly(2004, 1, 1);

        var r1 = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            PriceRequest("AGG", new DateOnly(2000, 1, 1), to)).ConfigureAwait(true);
        adapter.Dispatches.Should().ContainSingle("first request dispatches");

        // Second and third requests with different pre-inception `from` — the store's coverage now
        // starts at inception, so the pre-inception prefix (nothing exists there) is NOT re-fetched:
        // coverage.From <= request.From after clipping, so no prefix gap remains.
        var r2 = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            PriceRequest("AGG", new DateOnly(2001, 5, 31), to)).ConfigureAwait(true);
        var r3 = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            PriceRequest("AGG", new DateOnly(2001, 6, 14), to)).ConfigureAwait(true);

        adapter.Dispatches.Should().ContainSingle("second/third requests are fully subsumed — no new dispatch");
        r2.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Cache);
        r3.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Cache);
    }

    // AC-8 · Multi-dataset isolation. Ten distinct tickers appended; each request returns only its
    // own dataset's records — no cross-contamination through the shared engine.
    [Fact]
    public async Task AC8_multi_dataset_isolation()
    {
        var adapter = new CountingBarAdapter("tiingo", _ => throw new InvalidOperationException("no dispatch"));

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 1, 31);

        for (var i = 0; i < 10; i++)
        {
            var ticker = $"SYM{i:D2}";
            // Encode the dataset index into the Volume so cross-contamination is detectable.
            await store.AppendAsync(KeyFor(ticker),
            [
                new Bar(from, 1m, 1m, 1m, 1m, 1m, i),
                new Bar(to, 1m, 1m, 1m, 1m, 1m, i),
            ]).ConfigureAwait(true);
        }

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        for (var i = 0; i < 10; i++)
        {
            var result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
                PriceRequest($"SYM{i:D2}", from, to)).ConfigureAwait(true);
            result.Payload.Should().HaveCount(2, "both stored boundary records must survive");
            result.Payload.Select(bar => bar.Date).Should().Equal(from, to);
            result.Payload.Should().OnlyContain(b => b.Volume == i,
                "dataset SYM{0} must return only its own records", i);
        }

        adapter.Dispatches.Should().BeEmpty("every request is fully subsumed");
    }

    // AC-10 · Adapter count never exceeds gap count. Across a set of varied (from, to) requests, the
    // total adapter dispatches equal the sum of gaps produced — no redundant dispatch.
    [Fact]
    public async Task AC10_adapter_count_equals_gap_count()
    {
        var adapter = new CountingBarAdapter("tiingo", range => [Bar(range.From, 1m), Bar(range.To, 1m)]);

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var key = KeyFor("AGG");
        // Seed a covered interior window (2010..2020).
        await store.AppendAsync(key,
        [
            Bar(new DateOnly(2010, 1, 1), 100m),
            Bar(new DateOnly(2020, 12, 31), 100m),
        ]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        // Request 1: two gaps (prefix + suffix) ⇒ 2 dispatches.
        await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            PriceRequest("AGG", new DateOnly(2005, 1, 1), new DateOnly(2026, 12, 31))).ConfigureAwait(true);
        // Request 2: now fully covered ⇒ 0 dispatches.
        await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            PriceRequest("AGG", new DateOnly(2008, 1, 1), new DateOnly(2022, 12, 31))).ConfigureAwait(true);

        adapter.Dispatches.Should().HaveCount(2, "2 gaps on the first request, 0 on the fully-covered second");
    }

    // AC-6 · Staleness triggers tail fetch. The store's newest record is older than the staleness
    // window; a request whose To extends into the stale tail dispatches the adapter for the stale
    // tail only (§2.3 staleness note — GetTailAsync(ds, 1) newest-record probe).
    [Fact]
    public async Task AC6_staleness_triggers_tail_fetch()
    {
        var refetched = false;
        var adapter = new CountingBarAdapter("tiingo", range =>
        {
            refetched = true;
            return [Bar(range.From, 55m), Bar(range.To, 55m)];
        });
        // Clock is far past the newest stored record so the staleness window is exceeded.
        var clock = new FakeClock { UtcNow = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero) };

        using var sp = BuildKernel(
            svc =>
            {
                svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter);
                svc.AddSingleton<IClock>(clock);
            },
            opt =>
            {
                opt.UseTimeSeriesStore = true;
                opt.TimeSeriesStalenessMaxAge = TimeSpan.FromDays(7);
            });

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var key = KeyFor("AGG");
        // Coverage reaches the request's To (2026-06-20) so date-wise the request is fully subsumed
        // (zero dispatch). The newest record is ~6 months old vs the 2027-01-01 clock, exceeding the
        // 7-day staleness window, so the stale newest day is re-fetched anyway.
        await store.AppendAsync(key,
        [
            Bar(new DateOnly(2026, 1, 1), 100m),
            Bar(new DateOnly(2026, 6, 20), 100m),
        ]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = PriceRequest("AGG", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 20));

        var result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request)
            .ConfigureAwait(true);

        refetched.Should().BeTrue("a stale newest record forces a tail re-fetch even under full date coverage");
        adapter.Dispatches.Should().ContainSingle();
        adapter.Dispatches[0].From.Should().Be(new DateOnly(2026, 6, 20),
            "only the newest stale date needs refreshing, not the already-covered prefix");
        adapter.Dispatches[0].To.Should().Be(new DateOnly(2026, 6, 20));
        result.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Partial);
    }

    [Fact]
    public async Task Stale_tail_unions_prefix_and_suffix_without_duplicate_dispatch_or_coverage()
    {
        var first = new DateOnly(2026, 1, 5);
        var last = first.AddDays(2);
        var adapter = new CountingBarAdapter("default", range => Enumerable.Range(range.From.DayNumber,
            range.To.DayNumber - range.From.DayNumber + 1).Select(day => Bar(DateOnly.FromDayNumber(day), 10m)).ToArray());
        using var sp = BuildKernel(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter);
            svc.AddSingleton<IClock>(new FakeClock { UtcNow = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero) });
        }, options =>
        {
            options.UseTimeSeriesStore = true;
            options.TimeSeriesStalenessMaxAge = TimeSpan.FromDays(1);
        });
        var store = sp.GetRequiredService<ITimeSeriesStoreRegistry>().Resolve<PriceBarDatasetKey, Bar>()!;
        await store.AppendAsync(KeyFor("AGG"), [Bar(first.AddDays(1), 1m)]);
        var result = await sp.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<PriceHistoryRequest, Bar>(PriceRequest("AGG", first, last));
        adapter.Dispatches.Should().Equal(new DateRange(first, last));
        result.Payload.Should().HaveCount(3);
        result.Coverage.RequestedPoints.Should().Be(3);
        result.Coverage.ReturnedPoints.Should().Be(3);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    // F-L1L6-1 · Nightly extend-by-one-day driven the PRODUCTION way. Request 1 populates the store
    // THROUGH THE PIPELINE (gap-fill), which sets the attempted range for the dataset. Request 2 keeps
    // the same From but grows To by a new suffix beyond coverage. That suffix was NEVER attempted, so
    // it must be dispatched — it must not be clamped away as "known-empty" and served as stale Cache.
    // (AC-3 misses this because it pre-populates via store.AppendAsync directly, leaving the attempted
    // range unset so the leading-gap clamp is skipped.)
    [Fact]
    public async Task F_L1L6_1_pipeline_fetch_then_suffix_extend_dispatches_new_tail()
    {
        var adapter = new CountingBarAdapter("tiingo", range => [Bar(range.From, 3m), Bar(range.To, 4m)]);

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();

        // Request 1 — through the pipeline: the store is empty, so the whole range is a gap that is
        // fetched, appended, and recorded as attempted (attempted range = 2026-01-01 .. 2026-06-30).
        await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            PriceRequest("AGG", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30))).ConfigureAwait(true);
        adapter.Dispatches.Should().ContainSingle("first request fetches the whole range as one gap");

        // Request 2 — same From, To extended by a new trailing suffix beyond stored coverage.
        var r2 = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            PriceRequest("AGG", new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 5))).ConfigureAwait(true);

        adapter.Dispatches.Should().HaveCount(2,
            "the never-attempted trailing suffix (2026-07-01..2026-07-05) must be fetched, not clamped away");
        adapter.Dispatches[1].From.Should().Be(new DateOnly(2026, 7, 1));
        adapter.Dispatches[1].To.Should().Be(new DateOnly(2026, 7, 5));
        r2.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Partial,
            "a fresh tail merged with existing coverage is Partial, never stale Cache");
    }

    // F-L1L6-2 · A partially-failed gap fill must not poison L1. Call 1: prefix gap succeeds+appends,
    // suffix gap fails ⇒ Partial envelope naming the still-missing range. Call 2 (identical request):
    // the missing suffix MUST be re-attempted — not served from L1 as Cache over the incomplete range
    // for the whole TTL. (AC-25(b) misses this because it verifies durability by reading the store
    // directly, never issuing a second ExecuteAsync that would surface the poisoned L1 entry.)
    [Fact]
    public async Task F_L1L6_2_failed_partial_not_promoted_second_call_retries_failed_gap()
    {
        var suffixAttempts = 0;
        var adapter = new CountingBarAdapter("tiingo", range =>
        {
            // The SUFFIX gap (starts 2021) fails on its first attempt (transient upstream outage),
            // then heals on the retry; the prefix (starts 2005) always succeeds.
            if (range.From >= new DateOnly(2021, 1, 1))
            {
                suffixAttempts++;
                if (suffixAttempts == 1)
                {
                    throw new InvalidOperationException("suffix upstream down (transient)");
                }
            }

            return [Bar(range.From, 7m), Bar(range.To, 8m)];
        });

        using var sp = BuildKernel(
            svc => svc.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter),
            opt => opt.UseTimeSeriesStore = true);

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        await store.AppendAsync(KeyFor("AGG"),
        [
            Bar(new DateOnly(2010, 1, 1), 100m),
            Bar(new DateOnly(2020, 12, 31), 100m),
        ]).ConfigureAwait(true);

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = PriceRequest("AGG", new DateOnly(2005, 1, 1), new DateOnly(2026, 12, 31));

        // Call 1: prefix appends, suffix fails ⇒ Partial naming the still-missing range. A
        // known-incomplete result must NOT be promoted to L1.
        var r1 = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request).ConfigureAwait(true);
        r1.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Partial);
        var dispatchesAfterFirst = adapter.Dispatches.Count;

        // Call 2 (identical): must re-run the store flow and re-attempt the failed suffix — not be
        // served the stale prefix-only Partial from a poisoned L1 entry as Cache. The suffix heals,
        // so the now-complete tail (bars dated >= 2021) appears in the payload.
        var r2 = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request).ConfigureAwait(true);

        adapter.Dispatches.Count.Should().BeGreaterThan(dispatchesAfterFirst,
            "the incompletely-filled range must be re-attempted on the next call, not served from L1");
        r2.Provenance.Should().NotContain(p => p.RetrievalMode == RetrievalMode.Cache,
            "a known-incomplete range must never be returned as Cache");
        r2.Payload.Should().Contain(b => b.Date >= new DateOnly(2021, 1, 1),
            "the previously-failed suffix is re-fetched and now present — proof L1 was not poisoned");
    }
}
