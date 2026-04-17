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

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.ReferenceData;

public sealed class StandardBenchmarksTests
{
    [Fact]
    public void All_returns_six_benchmarks()
    {
        StandardBenchmarks.All.Should().HaveCount(6);
    }

    [Fact]
    public void All_contains_expected_members()
    {
        StandardBenchmarks.All.Should().BeEquivalentTo(new[]
        {
            StandardBenchmarks.UsdSofr,
            StandardBenchmarks.GbpSonia,
            StandardBenchmarks.EurEstr,
            StandardBenchmarks.CadCorra,
            StandardBenchmarks.ChfSaron,
            StandardBenchmarks.JpyTona,
        });
    }

    [Fact]
    public void All_returns_same_instance_each_call()
    {
        StandardBenchmarks.All.Should().BeSameAs(StandardBenchmarks.All);
    }

    [Fact]
    public void UsdSofr_metadata_matches_market_convention()
    {
        var b = StandardBenchmarks.UsdSofr;

        b.Name.Value.Should().Be("USD-SOFR");
        b.Currency.Should().Be(CurrencyCode.USD);
        b.Kind.Should().Be(BenchmarkKind.OvernightRiskFree);
        b.PublicationCalendarCode.Should().Be("USNY");
        b.AccrualDayCountCode.Should().Be("ACT/360");
        b.FixingLagBusinessDays.Should().Be(0);
    }

    [Fact]
    public void ChfSaron_uses_ACT_360_and_ZURH()
    {
        var b = StandardBenchmarks.ChfSaron;

        b.Currency.Should().Be(CurrencyCode.CHF);
        b.PublicationCalendarCode.Should().Be("CHZU");
        b.AccrualDayCountCode.Should().Be("ACT/360");
    }

    [Fact]
    public void JpyTona_uses_ACT_365F_and_TKYO()
    {
        var b = StandardBenchmarks.JpyTona;

        b.Currency.Should().Be(CurrencyCode.JPY);
        b.PublicationCalendarCode.Should().Be("JPTO");
        b.AccrualDayCountCode.Should().Be("ACT/365F");
    }

    [Theory]
    [InlineData("USD-SOFR")]
    [InlineData("usd-sofr")]
    [InlineData("Usd-Sofr")]
    public void ByName_matches_case_insensitively(string name)
    {
        StandardBenchmarks.ByName(name).Should().Be(StandardBenchmarks.UsdSofr);
    }

    [Fact]
    public void ByName_returns_null_for_unknown()
    {
        StandardBenchmarks.ByName("MADEUP-RATE").Should().BeNull();
    }
}
