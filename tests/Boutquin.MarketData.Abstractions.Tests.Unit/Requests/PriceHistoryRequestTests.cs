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

using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Requests;

public sealed class PriceHistoryRequestTests
{
    private static readonly DateRange s_range = new(
        new DateOnly(2024, 1, 1),
        new DateOnly(2024, 12, 31));

    [Fact]
    public void DatasetKey_ReturnsSymbolsEncodedAndOrdered()
    {
        var request = new PriceHistoryRequest([new Symbol("MSFT"), new Symbol("AAPL")], s_range);

        request.DatasetKey.Should().Be("_D2_004100410050004C_004D005300460054");
    }

    [Fact]
    public void PartitionKeys_ReturnsSymbols()
    {
        var symbols = new List<Symbol> { new("GOOG"), new("TSLA") };
        var request = new PriceHistoryRequest(symbols, s_range);

        // Defensive-copy contract: PartitionKeys returns the record's snapshot of the input,
        // not the caller's mutable reference. Assert element-equality but not
        // reference-equality — mutation of `symbols` after construction must not change the
        // request's partition keys.
        request.PartitionKeys.Should().Equal(symbols);
        request.PartitionKeys.Should().NotBeSameAs(symbols);
    }

    [Fact]
    public void Symbols_IsDefensivelyCopied()
    {
        var symbols = new List<Symbol> { new("MSFT") };
        var request = new PriceHistoryRequest(symbols, s_range);

        symbols.Add(new Symbol("AAPL"));

        request.Symbols.Should().ContainSingle().Which.Ticker.Should().Be("MSFT");
    }

    [Fact]
    public void Constructor_RejectsDuplicateSymbols_BeforeTheyCanBecomeDuplicatePartitions()
    {
        var act = () => new PriceHistoryRequest([new Symbol("AAPL"), new Symbol("AAPL")], s_range);

        act.Should().Throw<ArgumentException>().WithParameterName("symbols");
    }

    [Fact]
    public void WithSymbols_RejectsDuplicateSymbols_BeforeTheyCanBecomeDuplicatePartitions()
    {
        var request = new PriceHistoryRequest([new Symbol("AAPL")], s_range);

        var act = () => _ = request with { Symbols = [new Symbol("AAPL"), new Symbol("AAPL")] };

        act.Should().Throw<ArgumentException>().WithParameterName("symbols");
    }

    [Fact]
    public void DefaultFrequency_IsDaily()
    {
        var request = new PriceHistoryRequest([new Symbol("AAPL")], s_range);

        request.Frequency.Should().Be(DataFrequency.Daily);
    }

    // ---------- Exchange hint (Region-aware adapter routing) ----------

    [Fact]
    public void Exchange_DefaultsToNull_ForBackCompatWithUnscopedConsumers()
    {
        var request = new PriceHistoryRequest([new Symbol("AAPL")], s_range);

        request.Exchange.Should().BeNull();
    }

    [Fact]
    public void Exchange_IsPreserved_WhenProvided()
    {
        var request = new PriceHistoryRequest([new Symbol("XIC")], s_range) { Exchange = ExchangeCode.XTSE };

        request.Exchange.Should().Be(ExchangeCode.XTSE);
    }

    [Fact]
    public void DatasetKey_IsStable_ForSeparatorFreeSingleton()
    {
        // Safe singleton identity remains stable; ambiguous legacy batches must be re-fetched.
        var request = new PriceHistoryRequest([new Symbol("AAPL")], s_range);

        request.DatasetKey.Should().Be("AAPL");
    }

    [Fact]
    public void DatasetKey_IsScopedByExchange_WhenExchangeIsProvided()
    {
        var request = new PriceHistoryRequest([new Symbol("XIC")], s_range) { Exchange = ExchangeCode.XTSE };

        request.DatasetKey.Should().Be("XIC/XTSE");
    }

    [Fact]
    public void DatasetKey_DistinguishesExchangeScopedRequests_FromUnscopedAndFromOtherVenues()
    {
        // A request that says "AAPL on XNAS" is semantically different from "AAPL on XNYS"
        // and from venue-neutral "AAPL" — the L2 cache key must reflect this so two distinct
        // requests don't share a snapshot. We assert via DatasetKey rather than record
        // equality because the defensive-copy on Symbols (`[.. Symbols]`) always produces
        // a fresh internal list, making record-level equality unusable by design.
        var symbols = new[] { new Symbol("AAPL") };
        var unscoped = new PriceHistoryRequest(symbols, s_range);
        var onXnas = new PriceHistoryRequest(symbols, s_range) { Exchange = ExchangeCode.XNAS };
        var onXnys = new PriceHistoryRequest(symbols, s_range) { Exchange = ExchangeCode.XNYS };

        unscoped.DatasetKey.Should().NotBe(onXnas.DatasetKey);
        onXnas.DatasetKey.Should().NotBe(onXnys.DatasetKey);
        // Same exchange hint → identical DatasetKey.
        onXnas.DatasetKey.Should().Be(new PriceHistoryRequest(symbols, s_range) { Exchange = ExchangeCode.XNAS }.DatasetKey);
    }
}
