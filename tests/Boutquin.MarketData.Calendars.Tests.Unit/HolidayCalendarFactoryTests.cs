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

using Boutquin.MarketData.Calendars.Holidays;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Calendars.Tests.Unit;

/// <summary>
/// Tests for <see cref="HolidayCalendarFactory"/>.
/// </summary>
public sealed class HolidayCalendarFactoryTests
{
    [Fact]
    public void Create_USNY_ReturnsAlgorithmicCalendar()
    {
        // Act
        var calendar = HolidayCalendarFactory.Create("USNY");

        // Assert
        calendar.Should().NotBeNull();
        calendar.Should().BeOfType<UsNyseCalendar>();
    }

    [Fact]
    public void Create_GBLO_ReturnsCalendar()
    {
        // Act
        var calendar = HolidayCalendarFactory.Create("GBLO");

        // Assert
        calendar.Should().NotBeNull();
        calendar.Should().BeOfType<HolidayCalendar>();
    }

    [Fact]
    public void Create_TARGET_ReturnsCalendar()
    {
        // Act
        var calendar = HolidayCalendarFactory.Create("TARGET");

        // Assert
        calendar.Should().NotBeNull();
        calendar.Should().BeOfType<HolidayCalendar>();
    }

    [Fact]
    public void Create_CATO_ReturnsAlgorithmicCalendar()
    {
        // Act
        var calendar = HolidayCalendarFactory.Create("CATO");

        // Assert
        calendar.Should().NotBeNull();
        calendar.Should().BeOfType<CaTsxCalendar>();
    }

    [Fact]
    public void Create_XCME_ReturnsCalendar()
    {
        // Act
        var calendar = HolidayCalendarFactory.Create("XCME");

        // Assert
        calendar.Should().NotBeNull();
        calendar.Should().BeOfType<HolidayCalendar>();
    }

    [Fact]
    public void Create_UnknownCode_ReturnsWeekendOnly()
    {
        // Act — unknown code should NOT throw; it returns a WeekendOnlyCalendar.
        var calendar = HolidayCalendarFactory.Create("XYZQ");

        // Assert
        calendar.Should().NotBeNull();
        calendar.Should().BeOfType<WeekendOnlyCalendar>();
    }

    [Fact]
    public void SupportedCodes_ContainsAllSeven()
    {
        // Act
        var codes = HolidayCalendarFactory.SupportedCodes;

        // Assert
        codes.Should().HaveCount(7);
        codes.Should().Contain("USNY");
        codes.Should().Contain("GBLO");
        codes.Should().Contain("TARGET");
        codes.Should().Contain("CATO");
        codes.Should().Contain("XCME");
        codes.Should().Contain("USSF");
        codes.Should().Contain("CAST");
    }

    [Theory]
    [InlineData("USNY")]
    [InlineData("GBLO")]
    [InlineData("TARGET")]
    [InlineData("CATO")]
    [InlineData("XCME")]
    public void Created_Calendar_HasCorrectCodeProperty(string code)
    {
        // Act
        var calendar = HolidayCalendarFactory.Create(code);

        // Assert
        calendar.Code.Should().Be(code);
    }

    [Fact]
    public void USNY_Calendar_Christmas2024_IsHoliday()
    {
        // Arrange — Dec 25, 2024 is Wednesday and a USNY holiday.
        var calendar = HolidayCalendarFactory.Create("USNY");

        // Act
        var result = calendar.IsBusinessDay(new DateOnly(2024, 12, 25));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void USNY_Calendar_RandomTuesday_IsBusinessDay()
    {
        // Arrange — Jan 7, 2025 is a Tuesday with no holiday.
        var calendar = HolidayCalendarFactory.Create("USNY");

        // Act
        var result = calendar.IsBusinessDay(new DateOnly(2025, 1, 7));

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void XCME_Calendar_GoodFriday2024_IsHoliday()
    {
        // Arrange — Mar 29, 2024 is Good Friday, a CME full-closure day.
        var calendar = HolidayCalendarFactory.Create("XCME");

        // Act
        var result = calendar.IsBusinessDay(new DateOnly(2024, 3, 29));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void XCME_Calendar_MLKDay2024_IsBusinessDay()
    {
        // Arrange — Jan 15, 2024 is MLK Day. CME has early halt but still publishes
        // settlement prices, so it IS a business day for settlement purposes.
        var calendar = HolidayCalendarFactory.Create("XCME");

        // Act
        var result = calendar.IsBusinessDay(new DateOnly(2024, 1, 15));

        // Assert — CME stays open on MLK Day (unlike USNY/SIFMA).
        result.Should().BeTrue();
    }

    [Fact]
    public void XCME_Calendar_Thanksgiving2024_IsBusinessDay()
    {
        // Arrange — Nov 28, 2024 is Thanksgiving. CME halts early but publishes settlements.
        var calendar = HolidayCalendarFactory.Create("XCME");

        // Act
        var result = calendar.IsBusinessDay(new DateOnly(2024, 11, 28));

        // Assert
        result.Should().BeTrue();
    }
}
