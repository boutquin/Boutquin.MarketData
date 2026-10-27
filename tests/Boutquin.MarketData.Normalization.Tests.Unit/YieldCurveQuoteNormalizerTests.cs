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

public sealed class YieldCurveQuoteNormalizerTests
{
    private readonly YieldCurveQuoteNormalizer _sut = new();

    private static readonly DateOnly s_asOf = new(2026, 4, 10);

    [Fact]
    public void Rejects_null_required_tenor_with_member_diagnostic()
    {
        var act = () => _sut.Normalize(default);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("raw")
            .WithMessage("*Tenor*");
    }

    [Fact]
    public void Converts_percent_rate_to_decimal()
    {
        // Arrange — Bank of Canada publishes rates as 4.5 meaning 4.5%
        var quote = new YieldCurveQuote(s_asOf, "10Y", 4.5m);

        // Act
        var result = _sut.Normalize(quote);

        // Assert
        result.Rate.Should().Be(0.045m);
    }

    [Fact]
    public void Preserves_already_decimal_rate()
    {
        // Arrange — rate already in decimal form (< 1)
        var quote = new YieldCurveQuote(s_asOf, "10Y", 0.045m);

        // Act
        var result = _sut.Normalize(quote);

        // Assert
        result.Rate.Should().Be(0.045m);
    }

    [Fact]
    public void Uppercases_tenor_label()
    {
        // Arrange
        var quote = new YieldCurveQuote(s_asOf, "10y", 0.045m);

        // Act
        var result = _sut.Normalize(quote);

        // Assert
        result.Tenor.Should().Be("10Y");
    }

    [Fact]
    public void Trims_whitespace_from_tenor()
    {
        // Arrange
        var quote = new YieldCurveQuote(s_asOf, "  3M  ", 0.035m);

        // Act
        var result = _sut.Normalize(quote);

        // Assert
        result.Tenor.Should().Be("3M");
    }

    [Fact]
    public void Preserves_as_of_date()
    {
        // Arrange — the curve's as-of date must survive normalization unchanged.
        var quote = new YieldCurveQuote(s_asOf, "5Y", 3.98m);

        // Act
        var result = _sut.Normalize(quote);

        // Assert
        result.Date.Should().Be(s_asOf);
    }

    [Fact]
    public void Rate_of_one_is_treated_as_percent()
    {
        // Arrange — 1.0 is ambiguous, but >= 1 is treated as percent
        var quote = new YieldCurveQuote(s_asOf, "1Y", 1.0m);

        // Act
        var result = _sut.Normalize(quote);

        // Assert
        result.Rate.Should().Be(0.01m);
    }

    [Fact]
    public void Zero_rate_preserved()
    {
        // Arrange
        var quote = new YieldCurveQuote(s_asOf, "O/N", 0m);

        // Act
        var result = _sut.Normalize(quote);

        // Assert
        result.Rate.Should().Be(0m);
    }

    [Fact]
    public void Negative_rate_under_one_preserved_as_decimal()
    {
        // Arrange — negative yield (e.g., EUR short rates)
        var quote = new YieldCurveQuote(s_asOf, "3M", -0.005m);

        // Act
        var result = _sut.Normalize(quote);

        // Assert — absolute value < 1 → already decimal
        result.Rate.Should().Be(-0.005m);
    }

    [Fact]
    public void Negative_rate_over_one_converted_from_percent()
    {
        // Arrange — -1.5% as -1.5
        var quote = new YieldCurveQuote(s_asOf, "3M", -1.5m);

        // Act
        var result = _sut.Normalize(quote);

        // Assert
        result.Rate.Should().Be(-0.015m);
    }
}
