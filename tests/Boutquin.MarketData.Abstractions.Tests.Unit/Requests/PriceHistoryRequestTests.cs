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
    public void DatasetKey_ReturnsSymbolsJoinedOrdered()
    {
        var request = new PriceHistoryRequest([new Symbol("MSFT"), new Symbol("AAPL")], s_range);

        request.DatasetKey.Should().Be("AAPL+MSFT");
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
    public void DefaultFrequency_IsDaily()
    {
        var request = new PriceHistoryRequest([new Symbol("AAPL")], s_range);

        request.Frequency.Should().Be(DataFrequency.Daily);
    }
}
