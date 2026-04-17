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

using Boutquin.MarketData.Abstractions.Calendars;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Calendars.Tests.Unit;

/// <summary>
/// Tests for <see cref="WeekendOnlyCalendar"/>.
/// </summary>
public sealed class WeekendOnlyCalendarTests
{
    private readonly WeekendOnlyCalendar _sut = new();

    [Theory]
    [InlineData(2025, 1, 6)]  // Monday
    [InlineData(2025, 1, 7)]  // Tuesday
    [InlineData(2025, 1, 8)]  // Wednesday
    [InlineData(2025, 1, 9)]  // Thursday
    [InlineData(2025, 1, 10)] // Friday
    public void IsBusinessDay_Weekday_ReturnsTrue(int year, int month, int day)
    {
        // Arrange
        var date = new DateOnly(year, month, day);

        // Act
        var result = _sut.IsBusinessDay(date);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsBusinessDay_Saturday_ReturnsFalse()
    {
        // Arrange — Jan 11, 2025 is a Saturday
        var date = new DateOnly(2025, 1, 11);

        // Act
        var result = _sut.IsBusinessDay(date);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsBusinessDay_Sunday_ReturnsFalse()
    {
        // Arrange — Jan 12, 2025 is a Sunday
        var date = new DateOnly(2025, 1, 12);

        // Act
        var result = _sut.IsBusinessDay(date);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Adjust_Following_SkipsWeekendToMonday()
    {
        // Arrange — Jan 11, 2025 is Saturday; following business day is Monday Jan 13
        var saturday = new DateOnly(2025, 1, 11);

        // Act
        var result = _sut.Adjust(saturday, BusinessDayAdjustment.Following);

        // Assert
        result.Should().Be(new DateOnly(2025, 1, 13));
    }

    [Fact]
    public void Adjust_Preceding_RollsBackToFriday()
    {
        // Arrange — Jan 11, 2025 is Saturday; preceding business day is Friday Jan 10
        var saturday = new DateOnly(2025, 1, 11);

        // Act
        var result = _sut.Adjust(saturday, BusinessDayAdjustment.Preceding);

        // Assert
        result.Should().Be(new DateOnly(2025, 1, 10));
    }

    [Fact]
    public void Adjust_ModifiedFollowing_StaysInMonth()
    {
        // Arrange — Jan 31, 2026 is Saturday.
        // Following would roll to Mon Feb 2, which crosses the month boundary.
        // ModifiedFollowing falls back to Preceding: Friday Jan 30.
        var date = new DateOnly(2026, 1, 31);

        // Act
        var result = _sut.Adjust(date, BusinessDayAdjustment.ModifiedFollowing);

        // Assert
        result.Should().Be(new DateOnly(2026, 1, 30));
    }

    [Fact]
    public void Adjust_ModifiedPreceding_StaysInMonth()
    {
        // Arrange — Mar 1, 2026 is Sunday.
        // Preceding would roll to Fri Feb 27, which crosses the month boundary.
        // ModifiedPreceding falls back to Following: Monday Mar 2.
        var date = new DateOnly(2026, 3, 1);

        // Act
        var result = _sut.Adjust(date, BusinessDayAdjustment.ModifiedPreceding);

        // Assert
        result.Should().Be(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public void Adjust_None_ReturnsSameDate()
    {
        // Arrange — Saturday
        var saturday = new DateOnly(2025, 1, 11);

        // Act
        var result = _sut.Adjust(saturday, BusinessDayAdjustment.None);

        // Assert
        result.Should().Be(saturday);
    }

    [Fact]
    public void Advance_Positive_SkipsWeekends()
    {
        // Arrange — Friday Jan 10, 2025 + 1 business day = Monday Jan 13
        var friday = new DateOnly(2025, 1, 10);

        // Act
        var result = _sut.Advance(friday, 1);

        // Assert
        result.Should().Be(new DateOnly(2025, 1, 13));
    }

    [Fact]
    public void Advance_Negative_SkipsWeekends()
    {
        // Arrange — Monday Jan 13, 2025 - 1 business day = Friday Jan 10
        var monday = new DateOnly(2025, 1, 13);

        // Act
        var result = _sut.Advance(monday, -1);

        // Assert
        result.Should().Be(new DateOnly(2025, 1, 10));
    }

    [Fact]
    public void Advance_Zero_ReturnsSameDate()
    {
        // Arrange
        var date = new DateOnly(2025, 1, 8);

        // Act
        var result = _sut.Advance(date, 0);

        // Assert
        result.Should().Be(date);
    }

    [Fact]
    public void Advance_MultipleDays_CrossesWeekends()
    {
        // Arrange — Friday Jan 10, 2025 + 3 business days = Wednesday Jan 15
        // (Fri +1=Mon, +2=Tue, +3=Wed)
        var friday = new DateOnly(2025, 1, 10);

        // Act
        var result = _sut.Advance(friday, 3);

        // Assert
        result.Should().Be(new DateOnly(2025, 1, 15));
    }

    [Fact]
    public void Advance_IntMinValue_ThrowsArgumentOutOfRange()
    {
        // Math.Abs(int.MinValue) overflows; Advance must reject the value with a domain
        // exception rather than letting the arithmetic overflow bubble up.
        var date = new DateOnly(2025, 1, 10);

        var act = () => _sut.Advance(date, int.MinValue);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("businessDays");
    }
}
