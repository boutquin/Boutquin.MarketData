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

public sealed class ScalarObservationTests
{
    [Fact]
    public void Construction_PreservesAllFields()
    {
        // Arrange
        var date = new DateOnly(2024, 3, 1);

        // Act
        var obs = new ScalarObservation(date, 0.0525m, "decimal");

        // Assert
        obs.Date.Should().Be(date);
        obs.Value.Should().Be(0.0525m);
        obs.Units.Should().Be("decimal");
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        // Arrange
        var date = new DateOnly(2024, 3, 1);
        var obs1 = new ScalarObservation(date, 0.0525m, "decimal");
        var obs2 = new ScalarObservation(date, 0.0525m, "decimal");

        // Act & Assert
        obs1.Should().Be(obs2);
    }
}
