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
    public void Construction_PreservesFactorsDictionary()
    {
        // Arrange
        var date = new DateOnly(2024, 1, 15);
        var factors = new Dictionary<string, decimal>
        {
            ["Mkt-RF"] = 0.012m,
            ["SMB"] = -0.003m,
            ["HML"] = 0.005m
        };

        // Act
        var obs = new FactorObservation(date, factors);

        // Assert
        obs.Date.Should().Be(date);
        obs.Factors.Should().BeSameAs(factors);
        obs.Factors.Should().HaveCount(3);
        obs.Factors["Mkt-RF"].Should().Be(0.012m);
    }

    [Fact]
    public void Equality_SameDictionaryInstance_AreEqual()
    {
        // Arrange — record equality uses reference equality for IReadOnlyDictionary,
        // so two instances with the same dictionary reference are equal.
        var date = new DateOnly(2024, 1, 15);
        var factors = new Dictionary<string, decimal>
        {
            ["Mkt-RF"] = 0.012m,
            ["SMB"] = -0.003m
        };
        var obs1 = new FactorObservation(date, factors);
        var obs2 = new FactorObservation(date, factors);

        // Act & Assert
        obs1.Should().Be(obs2);
    }
}
