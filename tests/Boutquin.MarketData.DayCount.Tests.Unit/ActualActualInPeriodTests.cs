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

public sealed class ActualActualInPeriodTests
{
    [Theory]
    [InlineData(-1, 1, "start")]
    [InlineData(1, 11, "end")]
    [InlineData(-1, -1, "start")]
    [InlineData(11, 11, "end")]
    public void Interval_outside_coupon_period_is_rejected(int startOffset, int endOffset, string parameter)
    {
        var periodStart = new DateOnly(2026, 1, 1);
        var act = () => ActualActualInPeriod.Instance.YearFraction(
            periodStart.AddDays(startOffset), periodStart.AddDays(endOffset),
            periodStart, periodStart.AddDays(10), 2);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(parameter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Empty_interval_at_coupon_boundary_is_accepted(int offset)
    {
        var periodStart = new DateOnly(2026, 1, 1);
        var date = periodStart.AddDays(offset);

        ActualActualInPeriod.Instance.YearFraction(date, date, periodStart, periodStart.AddDays(10), 2)
            .Should().Be(0);
    }

    [Theory]
    [InlineData(int.MaxValue, 2)]
    [InlineData(1073741824, 4)]
    public void Large_positive_frequency_returns_finite_positive_fraction(int frequency, int days)
    {
        var start = new DateOnly(2026, 1, 1);
        var end = start.AddDays(days);

        var result = ActualActualInPeriod.Instance.YearFraction(start, end, start, end, frequency);

        result.Should().BeApproximately(1.0 / frequency, 1e-20);
    }

    [Fact]
    public void Code_is_ACT_ACT()
    {
        ActualActualInPeriod.Instance.Code.Should().Be("ACT/ACT");
    }

    [Fact]
    public void Half_period_produces_half_of_one_over_frequency()
    {
        // 91 days of a 182-day semiannual coupon period in a leap year.
        var yf = ActualActualInPeriod.Instance.YearFraction(
            start: new DateOnly(2024, 1, 1),
            end: new DateOnly(2024, 4, 1),
            periodStart: new DateOnly(2024, 1, 1),
            periodEnd: new DateOnly(2024, 7, 1),
            couponFrequency: 2);

        yf.Should().Be(0.25);
    }

    [Fact]
    public void Full_period_produces_one_over_frequency()
    {
        // Semiannual coupon accruing the full period should return exactly 1/2.
        var yf = ActualActualInPeriod.Instance.YearFraction(
            start: new DateOnly(2026, 1, 15),
            end: new DateOnly(2026, 7, 15),
            periodStart: new DateOnly(2026, 1, 15),
            periodEnd: new DateOnly(2026, 7, 15),
            couponFrequency: 2);

        yf.Should().Be(0.5);
    }

    [Fact]
    public void Annual_frequency_full_period_returns_one()
    {
        var yf = ActualActualInPeriod.Instance.YearFraction(
            start: new DateOnly(2026, 1, 15),
            end: new DateOnly(2027, 1, 15),
            periodStart: new DateOnly(2026, 1, 15),
            periodEnd: new DateOnly(2027, 1, 15),
            couponFrequency: 1);

        yf.Should().Be(1.0);
    }

    [Fact]
    public void Quarter_of_annual_period_returns_quarter()
    {
        // Three months into a one-year period, annual frequency.
        var yf = ActualActualInPeriod.Instance.YearFraction(
            start: new DateOnly(2026, 1, 1),
            end: new DateOnly(2026, 4, 1),
            periodStart: new DateOnly(2026, 1, 1),
            periodEnd: new DateOnly(2027, 1, 1),
            couponFrequency: 1);

        // 90 / 365 ≈ 0.2465753...
        yf.Should().BeApproximately(90.0 / 365.0, 1e-12);
    }

    [Fact]
    public void Two_argument_overload_falls_back_to_ACT_365_25()
    {
        var yf = ActualActualInPeriod.Instance.YearFraction(
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1));

        yf.Should().BeApproximately(365.0 / 365.25, 1e-12);
    }

    [Fact]
    public void Zero_coupon_frequency_is_rejected()
    {
        var act = () => ActualActualInPeriod.Instance.YearFraction(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1),
            couponFrequency: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Degenerate_period_is_rejected()
    {
        var act = () => ActualActualInPeriod.Instance.YearFraction(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 7, 1),
            periodStart: new DateOnly(2026, 1, 1),
            periodEnd: new DateOnly(2026, 1, 1),
            couponFrequency: 2);

        act.Should().Throw<ArgumentException>();
    }
}
