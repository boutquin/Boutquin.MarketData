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

using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.Storage.Domain.Enums;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Orchestration.Tests.Unit.Pipeline;

// M-6 option defaults. Locks in:
//   • UseTimeSeriesStore defaults false — legacy L2 snapshot path stays active until M-11.
//   • TimeSeriesSegmentFormat defaults Columnar — spec §2.6 firm default.
//   • Legacy option defaults unchanged (L1CacheTtl = 1h, ContinueOnPartitionFailure = true).
public sealed class DataPipelineOptionsTests
{
    [Fact]
    public void UseTimeSeriesStore_DefaultsFalse()
    {
        var options = new DataPipelineOptions();

        options.UseTimeSeriesStore.Should().BeFalse();
    }

    [Fact]
    public void TimeSeriesSegmentFormat_DefaultsColumnar()
    {
        var options = new DataPipelineOptions();

        options.TimeSeriesSegmentFormat.Should().Be(TimeSeriesSegmentFormat.Columnar);
    }

    [Fact]
    public void Legacy_L1CacheTtl_Default_Unchanged()
    {
        var options = new DataPipelineOptions();

        options.L1CacheTtl.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Legacy_ContinueOnPartitionFailure_Default_Unchanged()
    {
        var options = new DataPipelineOptions();

        options.ContinueOnPartitionFailure.Should().BeTrue();
    }

    [Fact]
    public void All_Options_Are_Settable()
    {
        var options = new DataPipelineOptions
        {
            UseTimeSeriesStore = true,
            TimeSeriesSegmentFormat = TimeSeriesSegmentFormat.Row,
            L1CacheTtl = TimeSpan.FromMinutes(15),
            ContinueOnPartitionFailure = false,
        };

        options.UseTimeSeriesStore.Should().BeTrue();
        options.TimeSeriesSegmentFormat.Should().Be(TimeSeriesSegmentFormat.Row);
        options.L1CacheTtl.Should().Be(TimeSpan.FromMinutes(15));
        options.ContinueOnPartitionFailure.Should().BeFalse();
    }

    [Fact]
    public void TimeSeriesStalenessMaxAge_RejectsNegativeValues()
    {
        var options = new DataPipelineOptions();

        var act = () => options.TimeSeriesStalenessMaxAge = TimeSpan.FromTicks(-1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }
}
