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

using Boutquin.MarketData.Abstractions.Enums;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Observability;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.Orchestration.Pipeline;

using Boutquin.Storage.Domain.Enums;
using Boutquin.Storage.Domain.ValueObjects.ValueLog;
using Boutquin.Storage.Infrastructure.WriteAheadLog;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// F-L2L3-01 / F-L5-02 — the §2.7 store-shaping <c>DataPipelineOptions</c> properties documented as
/// driving DI default wiring must actually shape the DI-constructed stores (they were previously
/// shadowed by the builder/factory field defaults). Each test sets a non-default value and asserts
/// the constructed store/builder reflects it; the shipped defaults are preserved when unset.
/// </summary>
public sealed class KernelStoreShapingOptionsTests : IDisposable
{
    private const string RootTemplate = "/tmp/mktdata-shaping-unit/{record-type}";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "mktdata-shaping-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // TimeSeriesSegmentRootPath — set via configurePipeline (no explicit timeSeriesDirectory param),
    // build the kernel, resolve the registry (drains → builds every store → each store's WAL ctor
    // eagerly creates its {record-type} folder). The Bar store's folder must land under the
    // option's root, proving the resolved store honours the option rather than the default root.
    [Fact]
    public void TimeSeriesSegmentRootPath_ShapesResolvedStoreSegmentFolder()
    {
        var custom = Path.Combine(_root, "opt-root");

        var services = new ServiceCollection();
        services.AddMarketDataKernel(configurePipeline: o =>
            o.TimeSeriesSegmentRootPath = Path.Combine(custom, "{record-type}"));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ITimeSeriesStoreRegistry>();

        Directory.Exists(Path.Combine(custom, "Bar")).Should().BeTrue(
            "the TimeSeriesSegmentRootPath option must shape the DI-constructed store's on-disk root");
    }

    [Fact]
    public void TimeSeriesSegmentRootPath_WithoutRecordTypeToken_FailsAtComposition()
    {
        var sharedRoot = Path.Combine(_root, "shared");
        var services = new ServiceCollection();

        var act = () => services.AddMarketDataKernel(configurePipeline: o =>
            o.TimeSeriesSegmentRootPath = sharedRoot);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("configurePipeline")
            .WithMessage("*{record-type}*shared folder*");
    }

    // TimeSeriesCompactionStrategy — flows from the option into the columnar builder that constructs
    // the store (previously shadowed by the builder's SizeTiered field default).
    [Fact]
    public void TimeSeriesCompactionStrategy_FlowsIntoColumnarBuilder()
    {
        var options = new DataPipelineOptions
        {
            TimeSeriesCompactionStrategy = TimeSeriesCompactionStrategy.Leveled,
        };
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar");

        ServiceCollectionExtensions.ApplyStoreShapingOptions(builder, RootTemplate, options);

        builder.CompactionStrategy.Should().Be(TimeSeriesCompactionStrategy.Leveled);
    }

    // TimeSeriesSegmentFormat — a Row default flips even a columnar-capable record's store to row.
    [Fact]
    public void TimeSeriesSegmentFormat_FlowsIntoColumnarBuilder()
    {
        var options = new DataPipelineOptions
        {
            TimeSeriesSegmentFormat = TimeSeriesSegmentFormat.Row,
        };
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar");

        ServiceCollectionExtensions.ApplyStoreShapingOptions(builder, RootTemplate, options);

        builder.SegmentFormat.Should().Be(TimeSeriesSegmentFormat.Row);
    }

    // TimeSeriesWriteAheadLog — WAL tuning flows into the builder (previously shadowed by the
    // builder's 256-batch field default).
    [Fact]
    public void TimeSeriesWriteAheadLog_FlowsIntoColumnarBuilder()
    {
        var options = new DataPipelineOptions
        {
            TimeSeriesWriteAheadLog = new WriteAheadLogOptions { GroupCommitBatchSize = 1 },
        };
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar");

        ServiceCollectionExtensions.ApplyStoreShapingOptions(builder, RootTemplate, options);

        builder.WriteAheadLogOptions.GroupCommitBatchSize.Should().Be(1);
    }

    [Theory]
    [InlineData(999, 0.01)]
    [InlineData(131_072, 0.02)]
    public void Nondefault_bloom_configuration_is_supported(int expectedItems, double rate)
    {
        var services = new ServiceCollection();
        Action compose = () => services.AddMarketDataKernel(configurePipeline: options =>
            options.TimeSeriesBloomFilter = new() { ExpectedItems = expectedItems, FalsePositiveRate = rate });

        compose.Should().NotThrow();
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ITimeSeriesStoreRegistration));
    }

    [Fact]
    public void Default_bloom_configuration_remains_supported()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel();
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(ITimeSeriesStoreRegistration));
    }

    [Fact]
    public void Quarantine_options_flow_into_columnar_builder()
    {
        var sink = new CapturingIncidentSink();
        var options = new DataPipelineOptions
        {
            QuarantineBehavior = QuarantineBehavior.FailQuery,
            DataQualityIncidentSink = sink,
        };
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar");

        ServiceCollectionExtensions.ApplyStoreShapingOptions(builder, RootTemplate, options);

        builder.QuarantineBehavior.Should().Be(QuarantineBehavior.FailQuery);
        builder.DataQualityIncidentSink.Should().BeSameAs(sink);
    }

    // TimeSeriesValueLog — a finite threshold enables WiscKey dispatch, but ONLY on the row format
    // (columnar rejects non-inline entries at the Storage layer), so it must be paired with Row.
    [Fact]
    public void TimeSeriesValueLog_FiniteThreshold_EnablesWiscKey_OnRowFormat()
    {
        var options = new DataPipelineOptions
        {
            TimeSeriesSegmentFormat = TimeSeriesSegmentFormat.Row,
            TimeSeriesValueLog = ValueLogOptions.Default with { ValueLogThresholdBytes = 2048 },
        };
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar");

        ServiceCollectionExtensions.ApplyStoreShapingOptions(builder, RootTemplate, options);

        builder.ValueLogEnabled.Should().BeTrue();
    }

    // Defaults preserved — a caller who sets nothing gets exactly today's columnar builder shape.
    [Fact]
    public void DefaultOptions_PreserveShippedColumnarDefaults()
    {
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar");

        ServiceCollectionExtensions.ApplyStoreShapingOptions(builder, RootTemplate, new DataPipelineOptions());

        builder.SegmentFormat.Should().Be(TimeSeriesSegmentFormat.Columnar);
        builder.CompactionStrategy.Should().Be(TimeSeriesCompactionStrategy.SizeTiered);
        builder.WriteAheadLogOptions.GroupCommitBatchSize.Should().Be(256);
        builder.BloomFilterOptions.ExpectedItems.Should().Be(131_072);
        builder.ValueLogEnabled.Should().BeFalse();
    }

    // Row store — compaction / WAL / value-log flow into the row options (previously shadowed by the
    // RowTimeSeriesStoreOptions field defaults). Value-log off by default (null option).
    [Fact]
    public void RowStore_ShapingOptions_FlowIntoRowOptions()
    {
        var options = new DataPipelineOptions
        {
            TimeSeriesCompactionStrategy = TimeSeriesCompactionStrategy.Leveled,
            TimeSeriesWriteAheadLog = new WriteAheadLogOptions { GroupCommitBatchSize = 1 },
            TimeSeriesValueLog = ValueLogOptions.Default with { ValueLogThresholdBytes = 2048 },
        };
        var rowOptions = new RowTimeSeriesStoreOptions();

        ServiceCollectionExtensions.ApplyRowStoreShapingOptions(rowOptions, RootTemplate, options);

        rowOptions.CompactionStrategy.Should().Be(TimeSeriesCompactionStrategy.Leveled);
        rowOptions.WriteAheadLog.GroupCommitBatchSize.Should().Be(1);
        rowOptions.ValueLog.Should().NotBeNull("a finite value-log threshold enables WiscKey on a row store");
        rowOptions.SegmentRoot.Should().Be(RootTemplate);
    }

    // Row store — default options keep value-log off (null), preserving the shipped default.
    [Fact]
    public void RowStore_DefaultOptions_KeepValueLogOff()
    {
        var rowOptions = new RowTimeSeriesStoreOptions();

        ServiceCollectionExtensions.ApplyRowStoreShapingOptions(rowOptions, RootTemplate, new DataPipelineOptions());

        rowOptions.ValueLog.Should().BeNull("the null default keeps value-log off");
        rowOptions.CompactionStrategy.Should().Be(TimeSeriesCompactionStrategy.SizeTiered);
    }

    private sealed class CapturingIncidentSink : IDataQualityIncidentSink
    {
        public void Report(DataQualityIncident incident)
        {
        }
    }
}
