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

public sealed class Actual365FixedTests
{
    [Fact]
    public void YearFraction_OneYear365Days_Returns1Point0()
    {
        // 2025-01-01 to 2026-01-01 = 365 days
        var result = Actual365Fixed.Instance.YearFraction(
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 1, 1));

        result.Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void YearFraction_30Days_Returns30Over365()
    {
        var result = Actual365Fixed.Instance.YearFraction(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31));

        result.Should().BeApproximately(30.0 / 365.0, 1e-12);
    }

    [Fact]
    public void YearFraction_SameDate_ReturnsZero()
    {
        var result = Actual365Fixed.Instance.YearFraction(
            new DateOnly(2025, 6, 15),
            new DateOnly(2025, 6, 15));

        result.Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void YearFraction_LeapYear366Days_Returns366Over365()
    {
        // 2024-01-01 to 2025-01-01 = 366 days (2024 is a leap year)
        var result = Actual365Fixed.Instance.YearFraction(
            new DateOnly(2024, 1, 1),
            new DateOnly(2025, 1, 1));

        result.Should().BeApproximately(366.0 / 365.0, 1e-12);
    }

    [Fact]
    public void Code_ReturnsACT365F()
    {
        Actual365Fixed.Instance.Code.Should().Be("ACT/365F");
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        var a = Actual365Fixed.Instance;
        var b = Actual365Fixed.Instance;

        a.Should().BeSameAs(b);
    }
}
