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

using Boutquin.MarketData.Abstractions.Results;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Results;

public sealed class DataCoverageTests
{
    [Fact]
    public void Status_ReturnsComplete_WhenRatioIs1()
    {
        // Arrange
        var coverage = new DataCoverage(100, 100, 0, 1.0m);

        // Act & Assert
        coverage.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Fact]
    public void Status_ReturnsMissing_WhenRatioIs0()
    {
        // Arrange
        var coverage = new DataCoverage(100, 0, 100, 0.0m);

        // Act & Assert
        coverage.Status.Should().Be(CompletenessStatus.Missing);
    }

    [Fact]
    public void Status_ReturnsPartial_WhenBetween0And1()
    {
        // Arrange
        var coverage = new DataCoverage(100, 75, 25, 0.75m);

        // Act & Assert
        coverage.Status.Should().Be(CompletenessStatus.Partial);
    }

    [Fact]
    public void CoverageRatio_Computation()
    {
        // Arrange & Act
        var coverage = new DataCoverage(
            RequestedPoints: 200,
            ReturnedPoints: 150,
            MissingPoints: 50,
            CoverageRatio: 0.75m);

        // Assert
        coverage.RequestedPoints.Should().Be(200);
        coverage.ReturnedPoints.Should().Be(150);
        coverage.MissingPoints.Should().Be(50);
        coverage.CoverageRatio.Should().Be(0.75m);
    }
}
