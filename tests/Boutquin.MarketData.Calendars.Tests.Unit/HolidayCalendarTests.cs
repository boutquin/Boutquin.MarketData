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
/// Tests for <see cref="HolidayCalendar"/>.
/// </summary>
public sealed class HolidayCalendarTests
{
    [Fact]
    public void IsBusinessDay_Holiday_ReturnsFalse()
    {
        // Arrange — Dec 25, 2024 is Wednesday (a weekday), but it is a holiday.
        var holidays = new[] { new DateOnly(2024, 12, 25) };
        var sut = new HolidayCalendar("TEST", holidays);

        // Act
        var result = sut.IsBusinessDay(new DateOnly(2024, 12, 25));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsBusinessDay_NonHolidayWeekday_ReturnsTrue()
    {
        // Arrange — Dec 24, 2024 is Tuesday and not in the holiday set.
        var holidays = new[] { new DateOnly(2024, 12, 25) };
        var sut = new HolidayCalendar("TEST", holidays);

        // Act
        var result = sut.IsBusinessDay(new DateOnly(2024, 12, 24));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsBusinessDay_Weekend_ReturnsFalse()
    {
        // Arrange — Jan 11, 2025 is Saturday, not in holiday list but still non-business.
        var holidays = new[] { new DateOnly(2025, 1, 1) };
        var sut = new HolidayCalendar("TEST", holidays);

        // Act
        var result = sut.IsBusinessDay(new DateOnly(2025, 1, 11));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Adjust_Following_SkipsHolidayAndWeekend()
    {
        // Arrange — Good Friday 2024 is March 29 (Friday). Following rolls to next
        // business day. March 30 is Saturday, March 31 is Sunday, so Following = Monday April 1.
        var holidays = new[] { new DateOnly(2024, 3, 29) };
        var sut = new HolidayCalendar("TEST", holidays);

        // Act
        var result = sut.Adjust(new DateOnly(2024, 3, 29), BusinessDayAdjustment.Following);

        // Assert
        result.Should().Be(new DateOnly(2024, 4, 1));
    }

    [Fact]
    public void Adjust_Preceding_SkipsHoliday()
    {
        // Arrange — Good Friday 2024 is March 29 (Friday).
        // Preceding rolls back to Thursday March 28.
        var holidays = new[] { new DateOnly(2024, 3, 29) };
        var sut = new HolidayCalendar("TEST", holidays);

        // Act
        var result = sut.Adjust(new DateOnly(2024, 3, 29), BusinessDayAdjustment.Preceding);

        // Assert
        result.Should().Be(new DateOnly(2024, 3, 28));
    }

    [Fact]
    public void Advance_SkipsHolidaysAndWeekends()
    {
        // Arrange — Thursday March 28, 2024 + 1 business day.
        // March 29 is Good Friday (holiday), March 30 is Saturday, March 31 is Sunday.
        // Next business day is Monday April 1.
        var holidays = new[] { new DateOnly(2024, 3, 29) };
        var sut = new HolidayCalendar("TEST", holidays);

        // Act
        var result = sut.Advance(new DateOnly(2024, 3, 28), 1);

        // Assert
        result.Should().Be(new DateOnly(2024, 4, 1));
    }

    [Fact]
    public void Constructor_EmptyHolidays_BehavesLikeWeekendOnly()
    {
        // Arrange
        var sut = new HolidayCalendar("EMPTY", Array.Empty<DateOnly>());

        // Act & Assert — weekday should be business day
        sut.IsBusinessDay(new DateOnly(2025, 1, 6)).Should().BeTrue();  // Monday
        sut.IsBusinessDay(new DateOnly(2025, 1, 11)).Should().BeFalse(); // Saturday
    }

    [Fact]
    public void Constructor_DuplicateHolidays_NoError()
    {
        // Arrange — passing the same date twice should not throw (HashSet deduplicates).
        var christmas = new DateOnly(2024, 12, 25);
        var holidays = new[] { christmas, christmas };

        // Act
        var sut = new HolidayCalendar("DUP", holidays);

        // Assert
        sut.IsBusinessDay(christmas).Should().BeFalse();
    }
}
