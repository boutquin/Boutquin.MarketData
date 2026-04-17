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

public sealed class ScalarObservationNormalizerTests
{
    private readonly ScalarObservationNormalizer _sut = new();

    [Fact]
    public void Converts_percent_units_to_decimal()
    {
        // Arrange — FRED yields often come as "4.5" meaning 4.5%
        var obs = new ScalarObservation(new DateOnly(2024, 1, 1), 4.5m, "percent");

        // Act
        var result = _sut.Normalize(obs);

        // Assert
        result.Value.Should().Be(0.045m);
        result.Units.Should().Be("decimal");
    }

    [Fact]
    public void Converts_Percent_case_insensitive()
    {
        // Arrange
        var obs = new ScalarObservation(new DateOnly(2024, 1, 1), 2.75m, "Percent");

        // Act
        var result = _sut.Normalize(obs);

        // Assert
        result.Value.Should().Be(0.0275m);
        result.Units.Should().Be("decimal");
    }

    [Fact]
    public void Converts_pct_abbreviation()
    {
        // Arrange
        var obs = new ScalarObservation(new DateOnly(2024, 1, 1), 5.0m, "pct");

        // Act
        var result = _sut.Normalize(obs);

        // Assert
        result.Value.Should().Be(0.05m);
        result.Units.Should().Be("decimal");
    }

    [Fact]
    public void Preserves_decimal_units_unchanged()
    {
        // Arrange — already in decimal form
        var obs = new ScalarObservation(new DateOnly(2024, 1, 1), 0.045m, "decimal");

        // Act
        var result = _sut.Normalize(obs);

        // Assert
        result.Value.Should().Be(0.045m);
        result.Units.Should().Be("decimal");
    }

    [Fact]
    public void Normalizes_units_string_to_lowercase()
    {
        // Arrange
        var obs = new ScalarObservation(new DateOnly(2024, 1, 1), 100.5m, "Index");

        // Act
        var result = _sut.Normalize(obs);

        // Assert
        result.Units.Should().Be("index");
    }

    [Fact]
    public void Preserves_date()
    {
        // Arrange
        var date = new DateOnly(2024, 6, 15);
        var obs = new ScalarObservation(date, 42m, "index");

        // Act
        var result = _sut.Normalize(obs);

        // Assert
        result.Date.Should().Be(date);
    }

    [Fact]
    public void Zero_percent_converts_to_zero_decimal()
    {
        // Arrange
        var obs = new ScalarObservation(new DateOnly(2024, 1, 1), 0m, "percent");

        // Act
        var result = _sut.Normalize(obs);

        // Assert
        result.Value.Should().Be(0m);
        result.Units.Should().Be("decimal");
    }

    [Fact]
    public void Non_rate_units_preserved_with_value_unchanged()
    {
        // Arrange — GDP in billions of dollars
        var obs = new ScalarObservation(new DateOnly(2024, 1, 1), 25000m, "billions-of-dollars");

        // Act
        var result = _sut.Normalize(obs);

        // Assert
        result.Value.Should().Be(25000m);
        result.Units.Should().Be("billions-of-dollars");
    }
}
