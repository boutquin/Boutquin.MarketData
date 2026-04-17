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
using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.DayCount.Tests.Unit;

public sealed class Actual360Tests
{
    [Fact]
    public void YearFraction_OneYear365Days_Returns365Over360()
    {
        // 2025-01-01 to 2026-01-01 = 365 days
        var result = Actual360.Instance.YearFraction(
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 1, 1));

        result.Should().BeApproximately(365.0 / 360.0, 1e-12);
    }

    [Fact]
    public void YearFraction_30Days_Returns30Over360()
    {
        // 2024-01-01 to 2024-01-31 = 30 days
        var result = Actual360.Instance.YearFraction(
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31));

        result.Should().BeApproximately(30.0 / 360.0, 1e-12);
    }

    [Fact]
    public void YearFraction_SameDate_ReturnsZero()
    {
        var result = Actual360.Instance.YearFraction(
            new DateOnly(2025, 6, 15),
            new DateOnly(2025, 6, 15));

        result.Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void YearFraction_LeapYear366Days_Returns366Over360()
    {
        // 2024-01-01 to 2025-01-01 = 366 days (2024 is a leap year)
        var result = Actual360.Instance.YearFraction(
            new DateOnly(2024, 1, 1),
            new DateOnly(2025, 1, 1));

        result.Should().BeApproximately(366.0 / 360.0, 1e-12);
    }

    [Fact]
    public void Code_ReturnsACT360()
    {
        Actual360.Instance.Code.Should().Be("ACT/360");
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        var a = Actual360.Instance;
        var b = Actual360.Instance;

        a.Should().BeSameAs(b);
    }
}
