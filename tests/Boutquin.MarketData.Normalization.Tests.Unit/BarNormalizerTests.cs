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

public sealed class BarNormalizerTests
{
    private readonly BarNormalizer _sut = new();

    [Fact]
    public void Rounds_prices_to_configured_precision()
    {
        // Arrange — prices with excess decimals
        var bar = new Bar(
            new DateOnly(2024, 6, 15),
            Open: 150.123456789m,
            High: 155.987654321m,
            Low: 148.111111111m,
            Close: 152.999999999m,
            AdjustedClose: 152.888888888m,
            Volume: 1_000_000);

        // Act
        var result = _sut.Normalize(bar);

        // Assert — default 4 decimal places
        result.Open.Should().Be(150.1235m);
        result.High.Should().Be(155.9877m);
        result.Low.Should().Be(148.1111m);
        result.Close.Should().Be(153.0000m);
        result.AdjustedClose.Should().Be(152.8889m);
    }

    [Fact]
    public void Clamps_negative_volume_to_zero()
    {
        // Arrange
        var bar = new Bar(new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m, 105m, -500);

        // Act
        var result = _sut.Normalize(bar);

        // Assert
        result.Volume.Should().Be(0);
    }

    [Fact]
    public void Preserves_valid_bar_without_changes()
    {
        // Arrange — already clean data
        var bar = new Bar(new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m, 105m, 5000);

        // Act
        var result = _sut.Normalize(bar);

        // Assert
        result.Open.Should().Be(100m);
        result.High.Should().Be(110m);
        result.Low.Should().Be(90m);
        result.Close.Should().Be(105m);
        result.Volume.Should().Be(5000);
        result.Date.Should().Be(bar.Date);
    }

    [Fact]
    public void Swaps_high_and_low_when_inverted()
    {
        // Arrange — high < low (data error from source)
        var bar = new Bar(new DateOnly(2024, 1, 1), 100m, 90m, 110m, 105m, 105m, 1000);

        // Act
        var result = _sut.Normalize(bar);

        // Assert
        result.High.Should().Be(110m);
        result.Low.Should().Be(90m);
    }

    [Fact]
    public void Custom_precision_rounds_to_specified_decimals()
    {
        // Arrange
        var normalizer = new BarNormalizer(decimalPlaces: 2);
        var bar = new Bar(new DateOnly(2024, 1, 1), 100.456m, 110.789m, 90.123m, 105.555m, 105.555m, 100);

        // Act
        var result = normalizer.Normalize(bar);

        // Assert
        result.Open.Should().Be(100.46m);
        result.High.Should().Be(110.79m);
        result.Low.Should().Be(90.12m);
        result.Close.Should().Be(105.56m);
    }

    [Fact]
    public void Zero_volume_is_preserved()
    {
        // Arrange
        var bar = new Bar(new DateOnly(2024, 1, 1), 100m, 110m, 90m, 105m, 105m, 0);

        // Act
        var result = _sut.Normalize(bar);

        // Assert
        result.Volume.Should().Be(0);
    }
}
