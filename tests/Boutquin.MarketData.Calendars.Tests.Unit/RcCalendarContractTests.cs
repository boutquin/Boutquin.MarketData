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
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Calendars.Holidays;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Calendars.Tests.Unit;

public sealed class RcCalendarContractTests
{
    [Theory]
    [InlineData(8, true)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    public void Nyse_observes_Carter_mourning_closure_with_adjacent_day_controls(int day, bool expected)
    {
        // NYSE notice: https://www.nyse.com/publicdocs/nyse/markets/american-options/rule-interpretations/2025/National_Day_of_Mourning_20250102.pdf
        new UsNyseCalendar().IsBusinessDay(new DateOnly(2025, 1, day)).Should().Be(expected);
    }

    [Fact]
    public void Sofr_publication_excludes_Veterans_Day_but_not_Thanksgiving_Friday()
    {
        // ARRC: https://www.newyorkfed.org/medialibrary/Microsites/arrc/files/2021/Term_SOFR_Avgs_Conventions.pdf
        var calendar = HolidayCalendarFactory.Create(StandardBenchmarks.UsdSofr.PublicationCalendarCode);
        calendar.IsBusinessDay(new DateOnly(2024, 11, 11)).Should().BeFalse();
        calendar.IsBusinessDay(new DateOnly(2024, 11, 29)).Should().BeTrue();
        new UsNyseCalendar().IsBusinessDay(new DateOnly(2024, 11, 11)).Should().BeTrue();
    }

    [Fact]
    public void Sonia_publication_occurs_on_following_London_business_day()
    {
        // Bank of England: https://www.bankofengland.co.uk/markets/sonia-benchmark/sonia-key-features-and-policies
        var benchmark = StandardBenchmarks.GbpSonia;
        var calendar = HolidayCalendarFactory.Create(benchmark.PublicationCalendarCode);
        benchmark.PublicationLagBusinessDays.Should().Be(1);
        calendar.Advance(new DateOnly(2025, 1, 3), benchmark.PublicationLagBusinessDays)
            .Should().Be(new DateOnly(2025, 1, 6));
    }

    [Fact]
    public void Composite_rejects_null_code_array_and_members_at_construction()
    {
        var nullCode = () => new CompositeCalendar(null!, new WeekendOnlyCalendar());
        var nullArray = () => new CompositeCalendar("TEST", (IBusinessCalendar[])null!);
        var nullMember = () => new CompositeCalendar("TEST", new WeekendOnlyCalendar(), null!);
        nullCode.Should().Throw<ArgumentNullException>().WithParameterName("code");
        nullArray.Should().Throw<ArgumentNullException>().WithParameterName("calendars");
        nullMember.Should().Throw<ArgumentException>().WithParameterName("calendars");
    }

    [Fact]
    public void Composite_rejects_undefined_mode_at_construction()
    {
        var construct = () => new CompositeCalendar("TEST", (CalendarCompositionMode)99, new WeekendOnlyCalendar());
        construct.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("mode");
    }

    [Fact]
    public void Weekend_rejects_null_code_like_HolidayCalendar()
    {
        var construct = () => new WeekendOnlyCalendar(null!);
        construct.Should().Throw<ArgumentNullException>().WithParameterName("code");
    }
}
