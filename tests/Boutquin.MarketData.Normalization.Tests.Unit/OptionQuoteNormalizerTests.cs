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
using Boutquin.MarketData.Normalization.Normalizers;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Normalization.Tests.Unit;

public sealed class OptionQuoteNormalizerTests
{
    private static readonly Symbol s_aapl = new("AAPL");
    private static readonly Symbol s_spy = new("SPY");

    private readonly OptionQuoteNormalizer _sut = new();

    [Fact]
    public void Preserves_underlying()
    {
        var quote = new OptionQuote(
            new DateOnly(2024, 6, 15), s_aapl, new DateOnly(2024, 7, 19),
            195m, OptionType.Call, 5.2m, 5.4m, 5.3m, 0.25m, 1000, 500);

        var result = _sut.Normalize(quote);

        result.Underlying.Should().Be(s_aapl);
    }

    [Fact]
    public void Converts_percent_IV_to_decimal()
    {
        var quote = new OptionQuote(
            new DateOnly(2024, 6, 15), s_aapl, new DateOnly(2024, 7, 19),
            195m, OptionType.Call, 5.2m, 5.4m, 5.3m, 25.0m, 1000, 500);

        var result = _sut.Normalize(quote);

        result.ImpliedVolatility.Should().Be(0.25m);
    }

    [Fact]
    public void Preserves_already_decimal_IV()
    {
        var quote = new OptionQuote(
            new DateOnly(2024, 6, 15), s_aapl, new DateOnly(2024, 7, 19),
            195m, OptionType.Call, 5.2m, 5.4m, 5.3m, 0.25m, 1000, 500);

        var result = _sut.Normalize(quote);

        result.ImpliedVolatility.Should().Be(0.25m);
    }

    [Fact]
    public void Null_IV_stays_null()
    {
        var quote = new OptionQuote(
            new DateOnly(2024, 6, 15), s_aapl, new DateOnly(2024, 7, 19),
            195m, OptionType.Call, 5.2m, 5.4m, 5.3m, null, 1000, 500);

        var result = _sut.Normalize(quote);

        result.ImpliedVolatility.Should().BeNull();
    }

    [Fact]
    public void Rounds_prices_to_configured_precision()
    {
        var quote = new OptionQuote(
            new DateOnly(2024, 6, 15), s_aapl, new DateOnly(2024, 7, 19),
            195.123456m, OptionType.Call, 5.2345m, 5.4567m, 5.3456m, 0.25m, 1000, 500);

        var result = _sut.Normalize(quote);

        result.Strike.Should().Be(195.1235m);
        result.Bid.Should().Be(5.2345m);
        result.Ask.Should().Be(5.4567m);
        result.LastPrice.Should().Be(5.3456m);
    }

    [Fact]
    public void Clamps_negative_open_interest_to_zero()
    {
        var quote = new OptionQuote(
            new DateOnly(2024, 6, 15), s_aapl, new DateOnly(2024, 7, 19),
            195m, OptionType.Call, 5.2m, 5.4m, 5.3m, 0.25m, -100, 500);

        var result = _sut.Normalize(quote);

        result.OpenInterest.Should().Be(0);
    }

    [Fact]
    public void Clamps_negative_volume_to_zero()
    {
        var quote = new OptionQuote(
            new DateOnly(2024, 6, 15), s_aapl, new DateOnly(2024, 7, 19),
            195m, OptionType.Call, 5.2m, 5.4m, 5.3m, 0.25m, 1000, -50);

        var result = _sut.Normalize(quote);

        result.Volume.Should().Be(0);
    }

    [Fact]
    public void Null_prices_stay_null()
    {
        var quote = new OptionQuote(
            new DateOnly(2024, 6, 15), s_aapl, new DateOnly(2024, 7, 19),
            195m, OptionType.Put, null, null, null, null, 0, 0);

        var result = _sut.Normalize(quote);

        result.Bid.Should().BeNull();
        result.Ask.Should().BeNull();
        result.LastPrice.Should().BeNull();
    }

    [Fact]
    public void Preserves_date_expiry_type()
    {
        var quote = new OptionQuote(
            new DateOnly(2024, 6, 15), s_spy, new DateOnly(2024, 12, 20),
            500m, OptionType.Put, 10m, 11m, 10.5m, 0.18m, 5000, 1200);

        var result = _sut.Normalize(quote);

        result.Date.Should().Be(new DateOnly(2024, 6, 15));
        result.Expiry.Should().Be(new DateOnly(2024, 12, 20));
        result.Type.Should().Be(OptionType.Put);
    }
}
