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

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

public sealed class FuturesSettlementTests
{
    private static readonly FuturesProductCode s_sr3 = new("SR3");
    private static readonly ContractMonth s_jun2025 = new(2025, 6);

    [Fact]
    public void Construction_WithImpliedRate()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 20);

        // Act
        var settlement = new FuturesSettlement(date, s_sr3, s_jun2025, 95.50m, 4.50m);

        // Assert
        settlement.Date.Should().Be(date);
        settlement.ContractCode.Should().Be(s_sr3);
        settlement.ContractMonth.Should().Be(s_jun2025);
        settlement.SettlePrice.Should().Be(95.50m);
        settlement.ImpliedRate.Should().Be(4.50m);
    }

    [Fact]
    public void Construction_WithoutImpliedRate()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 20);

        // Act
        var settlement = new FuturesSettlement(date, s_sr3, s_jun2025, 95.50m, null);

        // Assert
        settlement.ImpliedRate.Should().BeNull();
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 20);
        var s1 = new FuturesSettlement(date, s_sr3, s_jun2025, 95.50m, 4.50m);
        var s2 = new FuturesSettlement(date, s_sr3, s_jun2025, 95.50m, 4.50m);

        // Act & Assert
        s1.Should().Be(s2);
    }
}
