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

public sealed class FactorObservationTests
{
    [Fact]
    public void Construction_PreservesAllFields()
    {
        // Arrange
        var date = new DateOnly(2024, 1, 15);

        // Act
        var obs = new FactorObservation(date, "Mkt-RF", 0.012m);

        // Assert
        obs.Date.Should().Be(date);
        obs.FactorName.Should().Be("Mkt-RF");
        obs.Value.Should().Be(0.012m);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        // Arrange — one record per (Date, FactorName, Value); record value equality applies.
        var date = new DateOnly(2024, 1, 15);
        var obs1 = new FactorObservation(date, "SMB", -0.003m);
        var obs2 = new FactorObservation(date, "SMB", -0.003m);

        // Act & Assert
        obs1.Should().Be(obs2);
    }

    [Fact]
    public void Equality_DifferentFactorName_AreNotEqual()
    {
        // Arrange — same date and value, different factor → distinct records.
        var date = new DateOnly(2024, 1, 15);
        var obs1 = new FactorObservation(date, "SMB", 0.012m);
        var obs2 = new FactorObservation(date, "HML", 0.012m);

        // Act & Assert
        obs1.Should().NotBe(obs2);
    }
}
