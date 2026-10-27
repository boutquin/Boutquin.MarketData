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

using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Calendars.Holidays;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Calendars.Tests.Unit;

public sealed class RcSofrPublicationCalendarTests
{
    [Theory]
    [InlineData(2021, 4, 2)]
    [InlineData(2021, 6, 18)]
    [InlineData(2023, 4, 7)]
    [InlineData(2023, 11, 10)]
    [InlineData(2026, 4, 3)]
    public void Sifma_trading_calendar_preserves_early_close_and_unobserved_dates(int year, int month, int day)
    {
        // SIFMA historical schedules: https://www.sifma.org/resources/general/us-holiday-archive
        // Juneteenth 2021: https://www.newyorkfed.org/markets/opolicy/operating_policy_210617
        // April 2026: https://www.sifma.org/news/press-releases/sifma-fixed-income-market-close-recommendations-in-the-u-s-the-u-k-and-japan-for-good-friday-easter-monday-holidays-2026
        HolidayCalendarFactory.Create("USSF").IsBusinessDay(new DateOnly(year, month, day)).Should().BeTrue();
    }

    [Theory]
    [InlineData(2021, 4, 2)]
    [InlineData(2023, 4, 7)]
    [InlineData(2026, 4, 3)]
    public void Sofr_publication_excludes_explicit_Fed_exceptions_to_Sifma_trading(int year, int month, int day)
    {
        // NY Fed publication notices: operating_policy_210303a, operating_policy_230308a,
        // https://www.newyorkfed.org/markets/opolicy/operating_policy_260312a
        var date = new DateOnly(year, month, day);
        var benchmark = StandardBenchmarks.UsdSofr;
        var publication = HolidayCalendarFactory.Create(benchmark.PublicationCalendarCode);
        publication.Code.Should().Be("USSOFR");
        publication.IsBusinessDay(date).Should().BeFalse();
        HolidayCalendarFactory.Create("USSF").IsBusinessDay(date).Should().BeTrue();
        publication.Advance(date.AddDays(-1), benchmark.PublicationLagBusinessDays).Should().Be(date.AddDays(3));
    }

    [Theory]
    [InlineData(2021, 6, 18)]
    [InlineData(2023, 11, 10)]
    [InlineData(2024, 11, 29)]
    [InlineData(2025, 1, 9)]
    public void Sofr_still_publishes_on_other_open_or_early_close_days(int year, int month, int day)
    {
        // Jan 9 2025 is an NYSE closure but SOFR publishes normally:
        // https://www.newyorkfed.org/markets/opolicy/operating_policy_250102
        var calendar = HolidayCalendarFactory.Create(StandardBenchmarks.UsdSofr.PublicationCalendarCode);
        calendar.IsBusinessDay(new DateOnly(year, month, day)).Should().BeTrue();
    }
}
