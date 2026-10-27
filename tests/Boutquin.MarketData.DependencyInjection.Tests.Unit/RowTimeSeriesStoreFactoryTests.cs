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
using Boutquin.MarketData.Abstractions.Records;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

public sealed class RowTimeSeriesStoreFactoryTests
{
    [Fact]
    public void Options_DefaultSegmentRoot_IsAbsoluteAndIndependentOfCurrentDirectory()
    {
        var options = new RowTimeSeriesStoreOptions();

        Path.IsPathFullyQualified(options.SegmentRoot).Should().BeTrue();
        options.SegmentRoot.Should().Be(
            Path.Combine(AppContext.BaseDirectory, ".marketdata-timeseries", "{record-type}"));
    }

    [Fact]
    public void Configurator_RejectsUndefinedCompactionStrategy()
    {
        var configurator = new RowStoreConfigurator();

        var act = () => configurator.WithCompactionStrategy((TimeSeriesCompactionStrategy)999);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("strategy");
    }

    [Fact]
    public async Task BuildStackAsync_RejectsUndefinedCompactionStrategyFromDirectOptions()
    {
        var options = new RowTimeSeriesStoreOptions
        {
            SegmentRoot = Path.Combine(Path.GetTempPath(), "unused-marketdata-test-root"),
            CompactionStrategy = (TimeSeriesCompactionStrategy)999,
        };

        var act = () => RowTimeSeriesStoreFactory.BuildStackAsync<PriceBarDatasetKey, Bar>("Bar", options);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("options");
    }
}
