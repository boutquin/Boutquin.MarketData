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
using Boutquin.MarketData.Orchestration.Pipeline;

using Boutquin.Storage.Domain.Enums;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Orchestration.Tests.Unit.Pipeline;

/// <summary>
/// AC-1 — a fresh <see cref="DataPipelineOptions"/> carries every §2.7 time-series-store
/// default exactly. These defaults are load-bearing: the DI builder reads them to construct
/// each store, so a drifted default silently mis-wires the cache tier.
/// </summary>
public sealed class DataPipelineOptionsDefaultsTests
{
    private static DataPipelineOptions Fresh() => new();

    [Fact]
    public void UseTimeSeriesStore_DefaultsToFalse()
        => Fresh().UseTimeSeriesStore.Should().BeFalse();

    [Fact]
    public void TimeSeriesSegmentFormat_DefaultsToColumnar()
        => Fresh().TimeSeriesSegmentFormat.Should().Be(TimeSeriesSegmentFormat.Columnar);

    [Fact]
    public void TimeSeriesCompactionStrategy_DefaultsToSizeTiered()
        => Fresh().TimeSeriesCompactionStrategy.Should().Be(TimeSeriesCompactionStrategy.SizeTiered);

    [Fact]
    public void TimeSeriesSegmentRootPath_DefaultsToInterpolationTemplate()
        => Fresh().TimeSeriesSegmentRootPath.Should().Be("$storage-root/timeseries/{record-type}");

    [Fact]
    public void TimeSeriesWriteAheadLog_DefaultsToGroupCommitContract()
    {
        var wal = Fresh().TimeSeriesWriteAheadLog;

        wal.GroupCommitBatchSize.Should().Be(256);
        wal.GroupCommitLatencyMs.Should().Be(5);
        wal.SegmentMaxBytes.Should().Be(64L * 1024L * 1024L);
    }

    [Fact]
    public void TimeSeriesBloomFilter_DefaultsTo131072At1Percent()
    {
        var bloom = Fresh().TimeSeriesBloomFilter;

        bloom.ExpectedItems.Should().Be(131_072);
        bloom.FalsePositiveRate.Should().Be(0.01);
    }

    [Fact]
    public void TimeSeriesValueLog_DefaultsToDisabled()
        => Fresh().TimeSeriesValueLog.Should().BeNull();

    [Fact]
    public void QuarantineBehavior_DefaultsToTreatAsGap()
        => Fresh().QuarantineBehavior.Should().Be(QuarantineBehavior.TreatAsGap);

    [Fact]
    public void DataQualityIncidentSink_DefaultsToNull()
        => Fresh().DataQualityIncidentSink.Should().BeNull();
}
