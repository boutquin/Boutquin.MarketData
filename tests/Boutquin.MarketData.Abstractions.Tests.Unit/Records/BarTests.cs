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

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

public sealed class BarTests
{
    [Fact]
    public void Construction_PreservesAllFields()
    {
        // Arrange
        var date = new DateOnly(2024, 6, 15);

        // Act
        var bar = new Bar(date, 100.50m, 105.00m, 99.25m, 103.75m, 102.10m, 1_500_000L);

        // Assert
        bar.Date.Should().Be(date);
        bar.Open.Should().Be(100.50m);
        bar.High.Should().Be(105.00m);
        bar.Low.Should().Be(99.25m);
        bar.Close.Should().Be(103.75m);
        bar.AdjustedClose.Should().Be(102.10m);
        bar.Volume.Should().Be(1_500_000L);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        // Arrange
        var date = new DateOnly(2024, 6, 15);
        var bar1 = new Bar(date, 100m, 105m, 99m, 103m, 102m, 1_000L);
        var bar2 = new Bar(date, 100m, 105m, 99m, 103m, 102m, 1_000L);

        // Act & Assert
        bar1.Should().Be(bar2);
    }

    [Fact]
    public void Inequality_DifferentPrice_NotEqual()
    {
        // Arrange
        var date = new DateOnly(2024, 6, 15);
        var bar1 = new Bar(date, 100m, 105m, 99m, 103m, 102m, 1_000L);
        var bar2 = new Bar(date, 100m, 105m, 99m, 104m, 102m, 1_000L);

        // Act & Assert
        bar1.Should().NotBe(bar2);
    }
}
