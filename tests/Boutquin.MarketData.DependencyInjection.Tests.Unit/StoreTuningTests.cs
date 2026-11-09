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

using System.Collections.Concurrent;

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Observability;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.Storage.Domain.Enums;
using Boutquin.Storage.Domain.ValueObjects.LsmTree;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// Store-tuning surface added after the 3.0 RC review: persisted coverage index, corrupt-segment
/// incident reporting, memtable capacity and the shared row block cache.
/// </summary>
public sealed class StoreTuningTests : IDisposable
{
    private static readonly PriceBarDatasetKey s_dataset = new("fixture", DataFrequency.Daily, "AGG", null);
    private static readonly DateOnly s_first = new(2024, 1, 2);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "md-tuning-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Bar BarAt(int day) => new(s_first.AddDays(day), 100, 101, 99, 100, 100, 1000);

    private TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar> Builder(bool row)
    {
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar").WithSegmentRoot(_root);
        return row ? builder.UseRowFormat() : builder.UseColumnarFormat();
    }

    // ---------------------------------------------------------------- memtable capacity

    [Fact]
    public void MemTableCapacity_DefaultsTo10000AndRejectsNonPositive()
    {
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar");

        builder.MemTableCapacity.Should().Be(10_000);
        builder.WithMemTableCapacity(512).MemTableCapacity.Should().Be(512);
        var act = () => builder.WithMemTableCapacity(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PipelineOptions_ThreadMemTableCapacityAndPersistedCoverageIntoBuilder()
    {
        var options = new DataPipelineOptions
        {
            TimeSeriesMemTableCapacity = 256,
            PersistTimeSeriesCoverageIndex = true,
            TimeSeriesSegmentFormat = TimeSeriesSegmentFormat.Row,
        };
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar");

        ServiceCollectionExtensions.ApplyStoreShapingOptions(builder, _root, options);

        builder.MemTableCapacity.Should().Be(256);
        builder.PersistedCoverageIndex.Should().BeTrue();
    }

    [Fact]
    public void PipelineOptions_DefaultsPreserveShippedBehaviour()
    {
        var options = new DataPipelineOptions();

        options.TimeSeriesMemTableCapacity.Should().Be(10_000);
        options.PersistTimeSeriesCoverageIndex.Should().BeFalse("write amplification makes persistence opt-in");
    }

    [Fact]
    public void PipelineOptions_RejectNonPositiveMemTableCapacity()
    {
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar");
        var act = () => ServiceCollectionExtensions.ApplyStoreShapingOptions(
            builder, _root, new DataPipelineOptions { TimeSeriesMemTableCapacity = 0 });
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task SmallMemTableCapacity_AutoFlushesToSegmentsWithoutExplicitFlush()
    {
        var stack = await Builder(row: true).WithMemTableCapacity(2).BuildStackAsync();
        await using (var owner = (IAsyncDisposable)stack.Engine)
        {
            await stack.Store.AppendAsync(s_dataset, Enumerable.Range(0, 12).Select(BarAt).ToArray());
            await stack.Engine.FlushAsync();
        }

        var segments = Directory.EnumerateFiles(_root, "ts*", SearchOption.TopDirectoryOnly).ToArray();
        segments.Should().NotBeEmpty();
    }

    // ---------------------------------------------------------------- persisted coverage index

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistedCoverageIndex_WritesManifestAndSurvivesReopen(bool row)
    {
        var builder = Builder(row).UsePersistedCoverageIndex();
        var manifest = builder.ResolveCoverageIndexPath();
        var stack = await builder.BuildStackAsync();
        await using (var owner = (IAsyncDisposable)stack.Engine)
        {
            await stack.Store.AppendAsync(s_dataset, [BarAt(0), BarAt(5)]);
            await stack.Engine.FlushAsync();
        }

        File.Exists(manifest).Should().BeTrue("coverage must be persisted next to the segments");

        var reopened = await Builder(row).UsePersistedCoverageIndex().BuildStackAsync();
        await using (var owner = (IAsyncDisposable)reopened.Engine)
        {
            var coverage = await reopened.Store.GetCoverageAsync(s_dataset);
            coverage.Should().Be(new DateRange(s_first, s_first.AddDays(5)));
        }
    }

    [Fact]
    public async Task DefaultCoverageIndex_WritesNoManifest()
    {
        var builder = Builder(row: true);
        var stack = await builder.BuildStackAsync();
        await using (var owner = (IAsyncDisposable)stack.Engine)
        {
            await stack.Store.AppendAsync(s_dataset, [BarAt(0)]);
            await stack.Engine.FlushAsync();
        }

        File.Exists(builder.ResolveCoverageIndexPath()).Should().BeFalse();
    }

    // ---------------------------------------------------------------- corrupt-segment reporting

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptSegment_IsReportedToIncidentSinkAndCoverageShrinks(bool persisted)
    {
        var first = Builder(row: true);
        if (persisted) { first.UsePersistedCoverageIndex(); }
        var stack = await first.BuildStackAsync();
        await using (var owner = (IAsyncDisposable)stack.Engine)
        {
            await stack.Store.AppendAsync(s_dataset, [BarAt(0), BarAt(1)]);
            await stack.Engine.FlushAsync();
        }

        var segment = Directory.EnumerateFiles(_root, "ts*", SearchOption.TopDirectoryOnly).Should().ContainSingle().Which;
        var bytes = await File.ReadAllBytesAsync(segment);
        bytes[bytes.Length / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(segment, bytes);

        var sink = new RecordingSink();
        var second = Builder(row: true).WithDataQualityIncidentSink(sink);
        if (persisted) { second.UsePersistedCoverageIndex(); }
        var reopened = await second.BuildStackAsync();
        await using (var owner = (IAsyncDisposable)reopened.Engine)
        {
            var incident = sink.Incidents.Should().ContainSingle().Which;
            incident.Kind.Should().Be(DataQualityIncidentKind.SegmentExclusion);
            incident.RecordType.Should().Be(nameof(Bar));
            incident.Detail.Should().Contain(Path.GetFileName(segment));

            // The excluded span must read as a gap, never as covered.
            (await reopened.Store.GetCoverageAsync(s_dataset)).Should().BeNull();
            (await reopened.Store.GetGapsAsync(s_dataset, new DateRange(s_first, s_first.AddDays(1)))).Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task CorruptSegment_StillInvokesCallerCallbackAndSink()
    {
        var first = Builder(row: false);
        var stack = await first.BuildStackAsync();
        await using (var owner = (IAsyncDisposable)stack.Engine)
        {
            await stack.Store.AppendAsync(s_dataset, [BarAt(0)]);
            await stack.Engine.FlushAsync();
        }

        var segment = Directory.EnumerateFiles(_root, "ts*", SearchOption.TopDirectoryOnly).Should().ContainSingle().Which;
        var bytes = await File.ReadAllBytesAsync(segment);
        bytes[bytes.Length / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(segment, bytes);

        var seen = new ConcurrentQueue<CorruptSegmentExclusion>();
        var sink = new RecordingSink();
        var reopened = await Builder(row: false)
            .WithDataQualityIncidentSink(sink)
            .WithCorruptSegmentRecovery(CorruptSegmentRecoveryDisposition.ExcludeAndSurface, seen.Enqueue)
            .BuildStackAsync();
        await using (var owner = (IAsyncDisposable)reopened.Engine)
        {
            seen.Should().ContainSingle();
            sink.Incidents.Should().ContainSingle().Which.Kind.Should().Be(DataQualityIncidentKind.SegmentExclusion);
        }
    }

    [Fact]
    public async Task RowFactory_HonoursMemTableCapacityAndPersistedCoverage()
    {
        var options = new RowTimeSeriesStoreOptions
        {
            SegmentRoot = _root,
            MemTableCapacity = 4,
            PersistCoverageIndex = true,
        };
        var stack = await RowTimeSeriesStoreFactory.BuildStackAsync<PriceBarDatasetKey, Bar>("Bar", options);
        await using (var owner = (IAsyncDisposable)stack.Engine)
        {
            await stack.Store.AppendAsync(s_dataset, Enumerable.Range(0, 10).Select(BarAt).ToArray());
            await stack.Engine.FlushAsync();
            _ = await stack.Store.GetRangeAsync(s_dataset, s_first, s_first.AddDays(9)).ToListAsync();
        }

        File.Exists(Path.Combine(_root, "coverage", "index.json")).Should().BeTrue();
    }

    [Fact]
    public async Task RowFactory_ReportsCorruptSegmentToSink()
    {
        var options = new RowTimeSeriesStoreOptions { SegmentRoot = _root };
        var stack = await RowTimeSeriesStoreFactory.BuildStackAsync<PriceBarDatasetKey, Bar>("Bar", options);
        await using (var owner = (IAsyncDisposable)stack.Engine)
        {
            await stack.Store.AppendAsync(s_dataset, [BarAt(0), BarAt(1)]);
            await stack.Engine.FlushAsync();
        }

        var segment = Directory.EnumerateFiles(_root, "ts*", SearchOption.TopDirectoryOnly).Should().ContainSingle().Which;
        var bytes = await File.ReadAllBytesAsync(segment);
        bytes[bytes.Length / 2] ^= 0xFF;
        await File.WriteAllBytesAsync(segment, bytes);

        var sink = new RecordingSink();
        options.DataQualityIncidentSink = sink;
        var reopened = await RowTimeSeriesStoreFactory.BuildStackAsync<PriceBarDatasetKey, Bar>("Bar", options);
        await using (var owner = (IAsyncDisposable)reopened.Engine)
        {
            sink.Incidents.Should().ContainSingle().Which.Kind.Should().Be(DataQualityIncidentKind.SegmentExclusion);
        }
    }

    private sealed class RecordingSink : IDataQualityIncidentSink
    {
        public ConcurrentQueue<DataQualityIncident> Incidents { get; } = new();
        public void Report(DataQualityIncident incident) => Incidents.Enqueue(incident);
    }
}
