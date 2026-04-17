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
/// Tests for <see cref="CompositeCalendar"/>.
/// </summary>
public sealed class CompositeCalendarTests
{
    [Fact]
    public void IsBusinessDay_HolidayInAnyCalendar_ReturnsFalse()
    {
        // Arrange — Calendar A has holiday on Jan 2, Calendar B does not.
        // Composite should treat Jan 2 as non-business because not all calendars agree.
        var calA = new HolidayCalendar("A", new[] { new DateOnly(2025, 1, 2) });
        var calB = new HolidayCalendar("B", Array.Empty<DateOnly>());
        var sut = new CompositeCalendar("A+B", calA, calB);

        // Act
        var result = sut.IsBusinessDay(new DateOnly(2025, 1, 2));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsBusinessDay_BusinessDayInAllCalendars_ReturnsTrue()
    {
        // Arrange — Jan 6, 2025 (Monday) is not a holiday in either calendar.
        var calA = new HolidayCalendar("A", new[] { new DateOnly(2025, 1, 2) });
        var calB = new HolidayCalendar("B", new[] { new DateOnly(2025, 1, 3) });
        var sut = new CompositeCalendar("A+B", calA, calB);

        // Act
        var result = sut.IsBusinessDay(new DateOnly(2025, 1, 6));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Adjust_UsesCombinedHolidays()
    {
        // Arrange — Jan 2, 2025 (Thursday) is holiday in A; Jan 3, 2025 (Friday) is holiday in B.
        // Following from Jan 2: Jan 2 is non-business (A holiday), Jan 3 is non-business (B holiday),
        // Jan 4 is Saturday, Jan 5 is Sunday, Jan 6 (Monday) is the next combined business day.
        var calA = new HolidayCalendar("A", new[] { new DateOnly(2025, 1, 2) });
        var calB = new HolidayCalendar("B", new[] { new DateOnly(2025, 1, 3) });
        var sut = new CompositeCalendar("A+B", calA, calB);

        // Act
        var result = sut.Adjust(new DateOnly(2025, 1, 2), BusinessDayAdjustment.Following);

        // Assert
        result.Should().Be(new DateOnly(2025, 1, 6));
    }

    [Fact]
    public void Advance_UsesCombinedHolidays()
    {
        // Arrange — Jan 1, 2025 (Wednesday) + 1 business day.
        // Jan 1 is a holiday in both (New Year's). Starting from Jan 1, advance 1:
        // Jan 2 is holiday in A (non-business), Jan 3 is holiday in B (non-business),
        // Jan 4 is Saturday, Jan 5 is Sunday, Jan 6 (Monday) is the first composite business day.
        var calA = new HolidayCalendar("A", new[] { new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 2) });
        var calB = new HolidayCalendar("B", new[] { new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 3) });
        var sut = new CompositeCalendar("A+B", calA, calB);

        // Act
        var result = sut.Advance(new DateOnly(2025, 1, 1), 1);

        // Assert
        result.Should().Be(new DateOnly(2025, 1, 6));
    }

    [Fact]
    public void SingleCalendar_Composite_BehavesLikeOriginal()
    {
        // Arrange — A composite with a single calendar should behave identically.
        var holidays = new[] { new DateOnly(2025, 1, 1) };
        var inner = new HolidayCalendar("SINGLE", holidays);
        var sut = new CompositeCalendar("WRAP", inner);

        // Act & Assert
        sut.IsBusinessDay(new DateOnly(2025, 1, 1)).Should().BeFalse();
        sut.IsBusinessDay(new DateOnly(2025, 1, 2)).Should().BeTrue();
        sut.Adjust(new DateOnly(2025, 1, 1), BusinessDayAdjustment.Following)
            .Should().Be(inner.Adjust(new DateOnly(2025, 1, 1), BusinessDayAdjustment.Following));
        sut.Advance(new DateOnly(2025, 1, 2), 1)
            .Should().Be(inner.Advance(new DateOnly(2025, 1, 2), 1));
    }
}
