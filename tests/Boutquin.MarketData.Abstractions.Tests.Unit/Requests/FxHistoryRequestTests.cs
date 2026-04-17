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

using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Requests;

public sealed class FxHistoryRequestTests
{
    private static readonly DateRange s_range = new(
        new DateOnly(2024, 1, 1),
        new DateOnly(2024, 12, 31));

    [Fact]
    public void DatasetKey_ReturnsPairsJoinedOrdered()
    {
        // EUR/USD sorts before USD/JPY by ToString() ordering.
        var pairs = new List<FxPair>
        {
            new(CurrencyCode.USD, CurrencyCode.JPY),
            new(CurrencyCode.EUR, CurrencyCode.USD),
        };
        var request = new FxHistoryRequest(pairs, s_range);

        request.DatasetKey.Should().Be("EUR/USD+USD/JPY");
    }

    [Fact]
    public void PartitionKeys_ReturnsPairs()
    {
        var pairs = new List<FxPair> { new(CurrencyCode.GBP, CurrencyCode.USD) };
        var request = new FxHistoryRequest(pairs, s_range);

        // Defensive-copy contract (see PriceHistoryRequestTests for rationale).
        request.PartitionKeys.Should().Equal(pairs);
        request.PartitionKeys.Should().NotBeSameAs(pairs);
    }

    [Fact]
    public void Pairs_IsDefensivelyCopied()
    {
        var pairs = new List<FxPair> { new(CurrencyCode.EUR, CurrencyCode.USD) };
        var request = new FxHistoryRequest(pairs, s_range);

        pairs.Add(new FxPair(CurrencyCode.GBP, CurrencyCode.JPY));

        request.Pairs.Should().ContainSingle()
            .Which.Should().Be(new FxPair(CurrencyCode.EUR, CurrencyCode.USD));
    }

    [Fact]
    public void DefaultFrequency_IsDaily()
    {
        var request = new FxHistoryRequest(
            new List<FxPair> { new(CurrencyCode.EUR, CurrencyCode.USD) },
            s_range);

        request.Frequency.Should().Be(DataFrequency.Daily);
    }
}
