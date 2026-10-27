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

public sealed class InstrumentMetadataRequestTests
{
    [Fact]
    public void DatasetKey_ReturnsSymbolsEncodedAndOrdered()
    {
        var request = new InstrumentMetadataRequest([new Symbol("MSFT"), new Symbol("AAPL")]);

        request.DatasetKey.Should().Be("_D2_004100410050004C_004D005300460054");
    }

    [Fact]
    public void Symbols_IsDefensivelyCopied()
    {
        var symbols = new List<Symbol> { new("MSFT") };
        var request = new InstrumentMetadataRequest(symbols);
        symbols.Add(new Symbol("AAPL"));

        request.Symbols.Should().ContainSingle().Which.Ticker.Should().Be("MSFT");
    }

    [Fact]
    public void WithSymbols_RejectsDuplicateSymbols_BeforeTheyCanBecomeDuplicatePartitions()
    {
        var request = new InstrumentMetadataRequest([new Symbol("AAPL")]);

        var act = () => _ = request with { Symbols = [new Symbol("AAPL"), new Symbol("AAPL")] };

        act.Should().Throw<ArgumentException>().WithParameterName("symbols");
    }

    // ---------- Exchange hint ----------

    [Fact]
    public void Exchange_DefaultsToNull_ForBackCompatWithUnscopedConsumers()
    {
        var request = new InstrumentMetadataRequest([new Symbol("AAPL")]);

        request.Exchange.Should().BeNull();
    }

    [Fact]
    public void Exchange_IsPreserved_WhenProvided()
    {
        var request = new InstrumentMetadataRequest([new Symbol("XIC")]) { Exchange = ExchangeCode.XTSE };

        request.Exchange.Should().Be(ExchangeCode.XTSE);
    }

    [Fact]
    public void DatasetKey_PreservesSafeSingletonAndEncodesLegacyBatch()
    {
        // Safe singleton identity remains stable; ambiguous legacy batches must be re-fetched.
        var request = new InstrumentMetadataRequest([new Symbol("AAPL")]);

        request.DatasetKey.Should().Be("AAPL");
        new InstrumentMetadataRequest([new Symbol("MSFT"), new Symbol("AAPL"), new Symbol("AGG")])
            .DatasetKey.Should().Be("_D3_004100410050004C_004100470047_004D005300460054");
    }

    [Fact]
    public void DatasetKey_IsScopedByExchange_WhenExchangeIsProvided()
    {
        var request = new InstrumentMetadataRequest([new Symbol("XIC")]) { Exchange = ExchangeCode.XTSE };

        request.DatasetKey.Should().Be("XIC/XTSE");
    }

    [Fact]
    public void DatasetKey_DistinguishesExchangeScopedRequests()
    {
        // The 2026-04-19 CA research bug: bare "XIC" went to Tiingo and returned Cimarex
        // Energy (US equity) rather than iShares S&P/TSX Capped Composite (TSX ETF). With
        // an Exchange hint, each venue gets its own cache entry and adapters can self-filter.
        var symbols = new[] { new Symbol("XIC") };
        var unscoped = new InstrumentMetadataRequest(symbols);
        var onXtse = new InstrumentMetadataRequest(symbols) { Exchange = ExchangeCode.XTSE };
        var onXnas = new InstrumentMetadataRequest(symbols) { Exchange = ExchangeCode.XNAS };

        unscoped.DatasetKey.Should().NotBe(onXtse.DatasetKey);
        onXtse.DatasetKey.Should().NotBe(onXnas.DatasetKey);
    }
}
