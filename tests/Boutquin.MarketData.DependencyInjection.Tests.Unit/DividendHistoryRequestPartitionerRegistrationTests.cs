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
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

public sealed class DividendHistoryRequestPartitionerRegistrationTests
{
    [Fact]
    public void AddMarketDataKernel_registers_an_owned_dividend_partitioner()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel();
        using var provider = services.BuildServiceProvider();
        var range = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 3, 31));
        var request = new DividendHistoryRequest(
            [new Symbol("SCHD"), new Symbol("VIG")], range)
        {
            Exchange = ExchangeCode.XNYS,
        };

        var partitions = provider
            .GetRequiredService<IRequestPartitioner<DividendHistoryRequest>>()
            .Partition(request);

        partitions.Should().HaveCount(2);
        partitions.Select(partition => partition.Symbols.Single().Ticker)
            .Should().Equal("SCHD", "VIG");
        partitions.Should().OnlyContain(partition => partition.Range == range);
        partitions.Should().OnlyContain(partition => partition.Exchange == ExchangeCode.XNYS);
        partitions.Select(partition => partition.DatasetKey)
            .Should().Equal("SCHD/XNYS", "VIG/XNYS");
    }

    [Fact]
    public void AddMarketDataKernel_does_not_replace_a_consumer_dividend_partitioner()
    {
        var services = new ServiceCollection();
        var consumerPartitioner = new ConsumerDividendPartitioner();
        services.AddSingleton<IRequestPartitioner<DividendHistoryRequest>>(consumerPartitioner);

        services.AddMarketDataKernel();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRequestPartitioner<DividendHistoryRequest>>()
            .Should().BeSameAs(consumerPartitioner);
    }

    private sealed class ConsumerDividendPartitioner : IRequestPartitioner<DividendHistoryRequest>
    {
        public IReadOnlyList<DividendHistoryRequest> Partition(DividendHistoryRequest request) =>
            [request];
    }
}
