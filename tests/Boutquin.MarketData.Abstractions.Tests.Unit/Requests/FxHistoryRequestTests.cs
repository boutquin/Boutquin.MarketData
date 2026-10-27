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

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Requests;

public sealed class FxHistoryRequestTests
{
    private static readonly DateRange s_range = new(
        new DateOnly(2024, 1, 1),
        new DateOnly(2024, 12, 31));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Pairs_WithNullElement_ThrowsAtBoundary(bool useInit, bool includeValidPair)
    {
        var pair = new FxPair(Currency.EUR, Currency.USD);
        FxPair[] invalidPairs = includeValidPair ? [pair, null!] : [null!];
        var original = new FxHistoryRequest([pair], s_range);

        var act = () => useInit
            ? original with { Pairs = invalidPairs }
            : new FxHistoryRequest(invalidPairs, s_range);

        act.Should().Throw<ArgumentException>().WithParameterName("pairs");
        original.DatasetKey.Should().Be("EUR/USD");
    }

    [Fact]
    public void DatasetKey_ReturnsPairsJoinedOrdered()
    {
        // EUR/USD sorts before USD/JPY by ToString() ordering.
        var pairs = new List<FxPair>
        {
            new(Currency.USD, Currency.JPY),
            new(Currency.EUR, Currency.USD),
        };
        var request = new FxHistoryRequest(pairs, s_range);

        request.DatasetKey.Should().Be("EUR/USD_USD/JPY");
    }

    [Fact]
    public void PartitionKeys_ReturnsPairs()
    {
        var pairs = new List<FxPair> { new(Currency.GBP, Currency.USD) };
        var request = new FxHistoryRequest(pairs, s_range);

        // Defensive-copy contract (see PriceHistoryRequestTests for rationale).
        request.PartitionKeys.Should().Equal(pairs);
        request.PartitionKeys.Should().NotBeSameAs(pairs);
    }

    [Fact]
    public void Pairs_IsDefensivelyCopied()
    {
        var pairs = new List<FxPair> { new(Currency.EUR, Currency.USD) };
        var request = new FxHistoryRequest(pairs, s_range);

        pairs.Add(new FxPair(Currency.GBP, Currency.JPY));

        request.Pairs.Should().ContainSingle()
            .Which.Should().Be(new FxPair(Currency.EUR, Currency.USD));
    }

    [Fact]
    public void WithPairs_RejectsDuplicatePairs_BeforeTheyCanBecomeDuplicatePartitions()
    {
        var pair = new FxPair(Currency.EUR, Currency.USD);
        var request = new FxHistoryRequest([pair], s_range);

        var act = () => _ = request with { Pairs = [pair, pair] };

        act.Should().Throw<ArgumentException>().WithParameterName("pairs");
    }

    [Fact]
    public void DefaultFrequency_IsDaily()
    {
        var request = new FxHistoryRequest(
            new List<FxPair> { new(Currency.EUR, Currency.USD) },
            s_range);

        request.Frequency.Should().Be(DataFrequency.Daily);
    }

    // ---------- Exchange hint ----------

    [Fact]
    public void Exchange_DefaultsToNull()
    {
        var request = new FxHistoryRequest(
            new List<FxPair> { new(Currency.EUR, Currency.USD) },
            s_range);

        request.Exchange.Should().BeNull();
    }

    [Fact]
    public void Exchange_IsPreserved_WhenProvided()
    {
        // FX is OTC and venue-neutral at the data level; the hint allows adapter
        // orchestration to bias toward a regional source where helpful (e.g. Bank of
        // Canada for CAD pairs, ECB for EUR pairs).
        var request = new FxHistoryRequest(
            new List<FxPair> { new(Currency.USD, Currency.CAD) },
            s_range)
        { Exchange = ExchangeCode.XTSE };

        request.Exchange.Should().Be(ExchangeCode.XTSE);
    }

    [Fact]
    public void DatasetKey_IsStable_WhenExchangeIsNull()
    {
        // Existing L2 snapshots for EUR/USD_USD/JPY must remain reachable — consumers
        // that do not opt into exchange scoping must produce the same key.
        var request = new FxHistoryRequest(
            new List<FxPair>
            {
                new(Currency.USD, Currency.JPY),
                new(Currency.EUR, Currency.USD),
            },
            s_range);

        request.DatasetKey.Should().Be("EUR/USD_USD/JPY");
    }

    [Fact]
    public void DatasetKey_IsScopedByExchange_WhenExchangeIsProvided()
    {
        var request = new FxHistoryRequest(
            new List<FxPair> { new(Currency.USD, Currency.CAD) },
            s_range)
        { Exchange = ExchangeCode.XTSE };

        request.DatasetKey.Should().Be("USD/CAD/XTSE");
    }

    // ---------- WithRange (time-series gap-fill, §2.3 step 6) ----------

    [Fact]
    public void WithRange_NarrowsRange_AndPreservesPairsFrequencyAndExchange()
    {
        var pairs = new List<FxPair>
        {
            new(Currency.EUR, Currency.USD),
            new(Currency.USD, Currency.JPY),
        };
        var request = new FxHistoryRequest(pairs, s_range, DataFrequency.Weekly)
        { Exchange = ExchangeCode.XTSE };
        var gap = new DateRange(new DateOnly(2024, 3, 1), new DateOnly(2024, 3, 31));

        var narrowed = ((IDataRequest)request).WithRange(gap);

        var fx = narrowed.Should().BeOfType<FxHistoryRequest>().Subject;
        fx.Range.Should().Be(gap, "only the range is narrowed to the gap");
        fx.Pairs.Should().Equal(pairs, "the currency-pair set is preserved");
        fx.Frequency.Should().Be(DataFrequency.Weekly, "the frequency is preserved");
        fx.Exchange.Should().Be(ExchangeCode.XTSE, "the venue hint is preserved");
    }
}
