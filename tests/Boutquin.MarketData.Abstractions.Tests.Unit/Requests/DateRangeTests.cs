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

using Boutquin.MarketData.Abstractions.Requests;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Requests;

public sealed class DateRangeTests
{
    [Fact]
    public void Construction_PreservesFromAndTo()
    {
        // Arrange
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 12, 31);

        // Act
        var range = new DateRange(from, to);

        // Assert
        range.From.Should().Be(from);
        range.To.Should().Be(to);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        // Arrange
        var range1 = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30));
        var range2 = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30));

        // Act & Assert
        range1.Should().Be(range2);
    }

    [Fact]
    public void InvertedRange_Throws()
    {
        var act = () => new DateRange(new DateOnly(2024, 6, 30), new DateOnly(2024, 1, 1));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("to");
    }

    [Fact]
    public void SameFromAndTo_IsValid()
    {
        var d = new DateOnly(2024, 6, 15);

        var range = new DateRange(d, d);

        range.From.Should().Be(d);
        range.To.Should().Be(d);
    }
}
