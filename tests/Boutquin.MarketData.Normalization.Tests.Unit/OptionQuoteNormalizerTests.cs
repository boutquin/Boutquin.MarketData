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
using Boutquin.MarketData.Normalization.Normalizers;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Normalization.Tests.Unit;

// M-2.7: OptionQuote slimmed to (Date, Bid?, Ask?, LastPrice?, ImpliedVolatility?, OpenInterest,
// Volume) — the contract identity (Underlying/Expiry/Strike/Type) moved into OptionQuoteDatasetKey
// (operator decision A / §2.5.1). The normalizer is ADAPTED (not retired): it keeps Bid/Ask/LastPrice
// rounding + ImpliedVolatility normalization + OpenInterest/Volume clamp on the slimmed body, and
// DROPS the Strike rounding because Strike is no longer a body field (its wire precision is governed
// by [DecimalScale(4)] on the key). The former Preserves_underlying / Preserves_date_expiry_type /
// Strike-rounding assertions are removed because those body fields no longer exist; every other
// assertion (Bid/Ask/LastPrice rounding, IV conversion, OI/Volume clamp, null-preservation) survives
// unchanged.
public sealed class OptionQuoteNormalizerTests
{
    private static readonly DateOnly s_date = new(2024, 6, 15);

    private readonly OptionQuoteNormalizer _sut = new();

    [Fact]
    public void Default_record_is_valid_under_required_string_policy()
    {
        var result = _sut.Normalize(default);

        result.Should().Be(default(OptionQuote));
    }

    [Fact]
    public void Converts_percent_IV_to_decimal()
    {
        var quote = new OptionQuote(s_date, 5.2m, 5.4m, 5.3m, 25.0m, 1000, 500);

        var result = _sut.Normalize(quote);

        result.ImpliedVolatility.Should().Be(0.25m);
    }

    [Fact]
    public void Preserves_already_decimal_IV()
    {
        var quote = new OptionQuote(s_date, 5.2m, 5.4m, 5.3m, 0.25m, 1000, 500);

        var result = _sut.Normalize(quote);

        result.ImpliedVolatility.Should().Be(0.25m);
    }

    [Fact]
    public void Null_IV_stays_null()
    {
        var quote = new OptionQuote(s_date, 5.2m, 5.4m, 5.3m, null, 1000, 500);

        var result = _sut.Normalize(quote);

        result.ImpliedVolatility.Should().BeNull();
    }

    [Fact]
    public void Rounds_prices_to_configured_precision()
    {
        var quote = new OptionQuote(s_date, 5.23456m, 5.45678m, 5.34567m, 0.25m, 1000, 500);

        var result = _sut.Normalize(quote);

        result.Bid.Should().Be(5.2346m);
        result.Ask.Should().Be(5.4568m);
        result.LastPrice.Should().Be(5.3457m);
    }

    [Fact]
    public void Clamps_negative_open_interest_to_zero()
    {
        var quote = new OptionQuote(s_date, 5.2m, 5.4m, 5.3m, 0.25m, -100, 500);

        var result = _sut.Normalize(quote);

        result.OpenInterest.Should().Be(0);
    }

    [Fact]
    public void Clamps_negative_volume_to_zero()
    {
        var quote = new OptionQuote(s_date, 5.2m, 5.4m, 5.3m, 0.25m, 1000, -50);

        var result = _sut.Normalize(quote);

        result.Volume.Should().Be(0);
    }

    [Fact]
    public void Null_prices_stay_null()
    {
        var quote = new OptionQuote(s_date, null, null, null, null, 0, 0);

        var result = _sut.Normalize(quote);

        result.Bid.Should().BeNull();
        result.Ask.Should().BeNull();
        result.LastPrice.Should().BeNull();
    }

    [Fact]
    public void Preserves_date()
    {
        var quote = new OptionQuote(new DateOnly(2024, 6, 15), 10m, 11m, 10.5m, 0.18m, 5000, 1200);

        var result = _sut.Normalize(quote);

        result.Date.Should().Be(new DateOnly(2024, 6, 15));
    }
}
