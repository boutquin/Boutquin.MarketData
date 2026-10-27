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

/// <summary>TSX historical dates and substitution rules checked against exchange notices.</summary>
public sealed class CaTsxCalendarTests
{
    [Theory]
    [InlineData(2009)]
    [InlineData(2015)]
    [InlineData(2020)]
    [InlineData(2026)]
    public void VictoriaDay_WhenMay25IsMonday_IsThePriorMonday(int year)
    {
        var calendar = new CaTsxCalendar();
        calendar.IsBusinessDay(new DateOnly(year, 5, 18)).Should().BeFalse();
        calendar.IsBusinessDay(new DateOnly(year, 5, 25)).Should().BeTrue();
    }

    [Theory]
    [InlineData(2004)]
    [InlineData(2010)]
    [InlineData(2021)]
    public void SaturdayChristmas_ObservesMondayAndTuesday_LeavesFridayOpen(int year)
    {
        var calendar = new CaTsxCalendar();
        calendar.IsBusinessDay(new DateOnly(year, 12, 24)).Should().BeTrue();
        calendar.IsBusinessDay(new DateOnly(year, 12, 27)).Should().BeFalse();
        calendar.IsBusinessDay(new DateOnly(year, 12, 28)).Should().BeFalse();
        calendar.IsBusinessDay(new DateOnly(year, 12, 29)).Should().BeTrue();
    }

    [Fact]
    public void SundayChristmas_ObservesMondayAndTuesday()
    {
        var calendar = new CaTsxCalendar();
        calendar.IsBusinessDay(new DateOnly(2022, 12, 23)).Should().BeTrue();
        calendar.IsBusinessDay(new DateOnly(2022, 12, 26)).Should().BeFalse();
        calendar.IsBusinessDay(new DateOnly(2022, 12, 27)).Should().BeFalse();
        calendar.IsBusinessDay(new DateOnly(2022, 12, 28)).Should().BeTrue();
    }

    [Fact]
    public void FridayChristmas_ObservesBoxingDayOnMonday()
    {
        var calendar = new CaTsxCalendar();
        calendar.IsBusinessDay(new DateOnly(2026, 12, 24)).Should().BeTrue();
        calendar.IsBusinessDay(new DateOnly(2026, 12, 25)).Should().BeFalse();
        calendar.IsBusinessDay(new DateOnly(2026, 12, 28)).Should().BeFalse();
        calendar.IsBusinessDay(new DateOnly(2026, 12, 29)).Should().BeTrue();
    }

    [Fact]
    public void NewYearAndCanadaDay_SaturdayObservanceIsMonday_NotFriday()
    {
        var calendar = new CaTsxCalendar();
        calendar.IsBusinessDay(new DateOnly(2021, 12, 31)).Should().BeTrue();
        calendar.IsBusinessDay(new DateOnly(2022, 1, 3)).Should().BeFalse();
        calendar.IsBusinessDay(new DateOnly(2023, 6, 30)).Should().BeTrue();
        calendar.IsBusinessDay(new DateOnly(2023, 7, 3)).Should().BeFalse();
    }

    [Fact]
    public void FamilyDay_StartsIn2008()
    {
        var calendar = new CaTsxCalendar();
        calendar.IsBusinessDay(new DateOnly(2007, 2, 19)).Should().BeTrue();
        calendar.IsBusinessDay(new DateOnly(2008, 2, 18)).Should().BeFalse();
    }

    [Fact]
    public void September2001_FullDayClosureIsSeptember12_EarlyCloseStillTrades()
    {
        var calendar = new CaTsxCalendar();
        calendar.IsBusinessDay(new DateOnly(2001, 9, 11)).Should().BeTrue();
        calendar.IsBusinessDay(new DateOnly(2001, 9, 12)).Should().BeFalse();
        calendar.IsBusinessDay(new DateOnly(2001, 9, 13)).Should().BeTrue();
    }
}
