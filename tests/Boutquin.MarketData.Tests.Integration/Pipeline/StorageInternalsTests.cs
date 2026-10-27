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

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.DependencyInjection;

using Boutquin.Storage.Domain.ValueObjects.ValueLog;
using Boutquin.Storage.Infrastructure.SortedStringTable;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// M-8 Storage-2.0-internal band (spec §5, AC-14): assertions that reach past the pipeline into the
/// on-disk segment bytes produced by the DEFAULT columnar wiring vs. the <c>UseRowFormat()</c>
/// per-record opt-out — proving the segment format is exercised end-to-end and that the opt-out
/// actually overrides the library default (routing to Storage 2.0's block-structured SSTable, not
/// a legacy layout).
/// </summary>
public sealed class StorageInternalsTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-storage-internals-{Guid.NewGuid():N}");

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
                // Best-effort cleanup — a still-open segment handle should not fail an asserted test.
            }
        }
    }

    // The .bsct file's OUTER envelope header (ColumnarSegmentFormat.HeaderMagic, internal) is
    // 'B','S','L','C' (LSM-Columnar); the INNER compressed block magic (SegmentMagic, internal) is
    // 'B','S','C','T'. Both are internal, so assert the LITERAL bytes.
    private static readonly byte[] s_columnarOuterHeaderMagic = [(byte)'B', (byte)'S', (byte)'L', (byte)'C'];
    private static readonly byte[] s_columnarInnerBlockMagic = [(byte)'B', (byte)'S', (byte)'C', (byte)'T'];

    private ServiceProvider BuildKernel(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_tempDir, "snapshots"),
            rawDocumentDirectory: Path.Combine(_tempDir, "raw"),
            checkpointDirectory: Path.Combine(_tempDir, "checkpoints"),
            eventLogDirectory: Path.Combine(_tempDir, "events"),
            timeSeriesDirectory: Path.Combine(_tempDir, "timeseries"));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static Bar Bar(DateOnly date, decimal close)
        => new(date, close, close, close, close, close, 1_000);

    private static PriceBarDatasetKey KeyFor(string ticker)
        => new("default", DataFrequency.Daily, ticker, null);

    private static List<Bar> Flush24Months()
    {
        var seed = new List<Bar>();
        var d = new DateOnly(2022, 1, 1);
        for (var i = 0; i < 400; i++)
        {
            seed.Add(Bar(d, 100m + i));
            d = d.AddDays(1);
        }

        return seed;
    }

    // AC-14 (columnar half) · Default wiring writes .bsct columnar segments whose outer envelope
    // header carries the columnar magic 'B','S','C','T'.
    [Fact]
    public async Task AC14_default_wiring_writes_columnar_bsct_segment_with_columnar_magic()
    {
        using var sp = BuildKernel();

        var registry = sp.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var engine = registry.ResolveEngine<PriceBarDatasetKey, Bar>();

        await store.AppendAsync(KeyFor("AGG"), Flush24Months()).ConfigureAwait(true);
        await engine.FlushAsync().ConfigureAwait(true);

        var tsDir = Path.Combine(_tempDir, "timeseries");
        var bsctFiles = Directory.GetFiles(tsDir, "*.bsct", SearchOption.AllDirectories);
        bsctFiles.Should().NotBeEmpty("the default columnar wiring flushes .bsct segment files");

        var bytes = await File.ReadAllBytesAsync(bsctFiles[0]).ConfigureAwait(true);
        bytes.Take(4).Should().Equal(s_columnarOuterHeaderMagic,
            "the columnar outer-envelope header opens with the 'BSLC' magic bytes");
        // The inner compressed blocks carry the 'BSCT' block magic (the .bsct extension is named
        // after the inner block, not the outer envelope).
        ContainsSubsequence(bytes, s_columnarInnerBlockMagic).Should().BeTrue(
            "the inner compressed block carries the 'BSCT' block magic");
    }

    // AC-14 (row half) · The per-record UseRowFormat() opt-out overrides the columnar default,
    // producing .sst segments whose footer carries the public SsTableFooter.Magic 'B','S','S','1'
    // and NO columnar magic.
    [Fact]
    public async Task AC14_row_optout_writes_sst_segment_with_bss1_footer_and_no_columnar_magic()
    {
        // Build the row store through the SAME production factory the DI
        // ConfigureTimeSeriesStore<Bar>(b => b.UseRowFormat()) path uses (AddRowTimeSeriesStore →
        // RowTimeSeriesStoreFactory.BuildStackAsync). Building the stack directly lets the test flush
        // the row engine (the DI RowTimeSeriesStoreRegistration registers only the store, not the
        // engine, so ResolveEngine would return the columnar default — not the row engine under test).
        var rowRoot = Path.Combine(_tempDir, "rowstore");
        var options = new RowTimeSeriesStoreOptions
        {
            SegmentRoot = Path.Combine(rowRoot, "{record-type}"),
        };
        var stack = await RowTimeSeriesStoreFactory
            .BuildStackAsync<PriceBarDatasetKey, Bar>(nameof(Bar), options)
            .ConfigureAwait(true);

        await stack.Store.AppendAsync(KeyFor("AGG"), Flush24Months()).ConfigureAwait(true);
        await stack.Engine.FlushAsync().ConfigureAwait(true);

        var sstFiles = Directory.GetFiles(rowRoot, "*.sst", SearchOption.AllDirectories);
        sstFiles.Should().NotBeEmpty("the row opt-out flushes .sst SSTable segment files");
        Directory.GetFiles(rowRoot, "*.bsct", SearchOption.AllDirectories)
            .Should().BeEmpty("the row opt-out must NOT produce columnar .bsct segments");

        var bytes = await File.ReadAllBytesAsync(sstFiles[0]).ConfigureAwait(true);

        // The public SsTableFooter.Magic ('B','S','S','1') sits at the head of the footer; scan for
        // it to prove the block-structured SSTable layout (not a legacy row format).
        SsTableFooter.Magic.Should().Equal([(byte)'B', (byte)'S', (byte)'S', (byte)'1'],
            "sanity: the public row-format footer magic is 'BSS1'");
        ContainsSubsequence(bytes, SsTableFooter.Magic).Should().BeTrue(
            "the row segment carries the SsTableFooter 'BSS1' footer magic");
        ContainsSubsequence(bytes, s_columnarOuterHeaderMagic).Should().BeFalse(
            "the row segment must NOT carry the columnar 'BSLC' outer magic");
        ContainsSubsequence(bytes, s_columnarInnerBlockMagic).Should().BeFalse(
            "the row segment must NOT carry the columnar 'BSCT' inner magic");
    }

    // AC-18 (WiscKey row roundtrip half) · A row-format store with a finite value-log threshold
    // dispatches an over-threshold record to the WiscKey value log (a ValueKind.ValueLogPointer SSTable
    // entry) and materialises the original record on read by dereferencing through ValueLogReader. The
    // observable proof at this layer: a valuelog segment file is produced (the pointer path fired) AND
    // the >threshold record roundtrips byte-identically (the dereference reconstructed it).
    [Fact]
    public async Task AC18_row_valuelog_roundtrips_over_threshold_record_through_value_log()
    {
        var rowRoot = Path.Combine(_tempDir, "wisckey-rowstore");
        var options = new RowTimeSeriesStoreOptions
        {
            SegmentRoot = Path.Combine(rowRoot, "{record-type}"),
            // WiscKey on: values strictly longer than 1024 bytes dispatch to the value log.
            ValueLog = new ValueLogOptions(ValueLogThresholdBytes: 1024),
        };

        var stack = await RowTimeSeriesStoreFactory
            .BuildStackAsync<ScalarObservationDatasetKey, ScalarObservation>(nameof(ScalarObservation), options)
            .ConfigureAwait(true);

        var key = new ScalarObservationDatasetKey("fred", "GDP");
        // A ScalarObservation whose Units string pushes the serialised record well past 1024 bytes,
        // forcing WiscKey value-log dispatch rather than an inline SSTable entry.
        var bigUnits = new string('U', 4096);
        var expected = new ScalarObservation(new DateOnly(2024, 6, 1), 3.14159m, bigUnits);
        await stack.Store.AppendAsync(key, [expected]).ConfigureAwait(true);
        await stack.Engine.FlushAsync().ConfigureAwait(true);

        // The value log directory holds at least one segment — proving the over-threshold value was
        // dispatched to WiscKey (a ValueLogPointer entry) rather than stored inline.
        var valueLogDir = Path.Combine(rowRoot, nameof(ScalarObservation), "valuelog");
        Directory.Exists(valueLogDir).Should().BeTrue("WiscKey dispatch creates the value-log directory");
        Directory.GetFiles(valueLogDir)
            .Any(f => !f.EndsWith(".manifest", StringComparison.Ordinal))
            .Should().BeTrue("the over-threshold value is written to a value-log segment");

        // Roundtrip: the read dereferences the pointer through ValueLogReader and reconstructs the
        // original record byte-for-byte (the long Units string survives intact).
        var read = new List<ScalarObservation>();
        await foreach (var r in stack.Store.GetRangeAsync(
            key, new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 1)).ConfigureAwait(true))
        {
            read.Add(r);
        }

        read.Should().ContainSingle("the single appended record roundtrips");
        read[0].Should().Be(expected, "the value-log dereference reconstructs the original record exactly");
        read[0].Units.Should().Be(bigUnits, "the >1KB payload survives the value-log roundtrip");
    }

    private static bool ContainsSubsequence(byte[] haystack, ReadOnlySpan<byte> needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
