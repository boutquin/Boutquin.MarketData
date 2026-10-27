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

using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit;

public sealed class BatchDataRequestTests
{
    private static readonly DateRange s_range = new(
        new DateOnly(2024, 1, 1),
        new DateOnly(2024, 12, 31));

    [Fact]
    public void PriceHistoryRequest_implements_IBatchDataRequest_of_Symbol()
    {
        var request = new PriceHistoryRequest([new Symbol("AAPL"), new Symbol("MSFT")], s_range);

        request.Should().BeAssignableTo<IBatchDataRequest<Symbol>>();
        ((IBatchDataRequest<Symbol>)request).PartitionKeys
            .Should().BeEquivalentTo([new Symbol("AAPL"), new Symbol("MSFT")]);
    }

    [Fact]
    public void FxHistoryRequest_implements_IBatchDataRequest_of_FxPair()
    {
        var pairs = new List<FxPair>
        {
            new(Currency.EUR, Currency.USD),
            new(Currency.GBP, Currency.USD),
        };
        var request = new FxHistoryRequest(pairs, s_range);

        request.Should().BeAssignableTo<IBatchDataRequest<FxPair>>();
        ((IBatchDataRequest<FxPair>)request).PartitionKeys.Should().BeEquivalentTo(pairs);
    }

    [Fact]
    public void EconomicSeriesRequest_does_not_implement_IBatchDataRequest()
    {
        var request = new EconomicSeriesRequest(new EconomicSeriesId("GDPC1"), s_range);

        request.Should().NotBeAssignableTo<IBatchDataRequest<string>>();
    }

    [Fact]
    public void PriceHistoryRequest_PartitionKeys_returns_same_reference_as_Symbols()
    {
        var symbols = new List<Symbol> { new("AAPL"), new("GOOG") };
        var request = new PriceHistoryRequest(symbols, s_range);

        request.PartitionKeys.Should().BeSameAs(request.Symbols);
    }
}
