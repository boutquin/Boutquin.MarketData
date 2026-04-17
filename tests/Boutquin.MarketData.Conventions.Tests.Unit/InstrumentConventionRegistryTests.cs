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

namespace Boutquin.MarketData.Conventions.Tests.Unit;

public sealed class InstrumentConventionRegistryTests
{
    [Fact]
    public void CreateDefault_contains_four_currency_OIS_entries()
    {
        var registry = InstrumentConventionRegistry.CreateDefault();

        foreach (var code in new[] { "USD-SOFR-OIS", "GBP-SONIA-OIS", "EUR-ESTR-OIS", "CAD-CORRA-OIS" })
        {
            registry.GetRequired(code).Should().NotBeNull();
            registry.GetRequired(code).Should().BeOfType<OisConvention>();
        }
    }

    [Fact]
    public void CreateDefault_includes_FRA_future_and_swap_legs()
    {
        var registry = InstrumentConventionRegistry.CreateDefault();

        registry.GetRequired("USD-FRA").Should().BeOfType<FraConvention>();
        registry.GetRequired("USD-SR3").Should().BeOfType<FutureContractConvention>();
        registry.GetRequired("USD-FIXED-6M-30-360").Should().BeOfType<SwapLegConvention>();
        registry.GetRequired("GBP-FIXED-1Y-ACT-365F").Should().BeOfType<SwapLegConvention>();
        registry.GetRequired("EUR-FIXED-1Y-ACT-360").Should().BeOfType<SwapLegConvention>();
    }

    [Fact]
    public void UsdSofr_default_matches_ARRC_convention()
    {
        var registry = InstrumentConventionRegistry.CreateDefault();

        var ois = (OisConvention)registry.GetRequired("USD-SOFR-OIS");

        ois.CalendarCode.Should().Be("USNY");
        ois.DayCountCode.Should().Be("ACT/360");
        ois.PaymentFrequency.Should().Be(PaymentFrequency.Annual);
        ois.BusinessDayAdjustment.Should().Be(BusinessDayAdjustment.ModifiedFollowing);
        ois.SettlementLagBusinessDays.Should().Be(2);
        ois.PaymentLagBusinessDays.Should().Be(2);
        ois.ObservationShiftConvention.Should().Be(ObservationShiftConvention.Shifted);
    }

    [Fact]
    public void Add_last_write_wins()
    {
        var registry = new InstrumentConventionRegistry();

        registry.Add(new FraConvention("USD-FRA", "USNY", "ACT/360", BusinessDayAdjustment.Following, 2));
        registry.Add(new FraConvention("USD-FRA", "USNY", "ACT/360", BusinessDayAdjustment.ModifiedFollowing, 3));

        var fra = (FraConvention)registry.GetRequired("USD-FRA");
        fra.BusinessDayAdjustment.Should().Be(BusinessDayAdjustment.ModifiedFollowing);
        fra.SettlementLagBusinessDays.Should().Be(3);
    }

    [Fact]
    public void GetRequired_is_case_insensitive()
    {
        var registry = InstrumentConventionRegistry.CreateDefault();

        registry.GetRequired("usd-sofr-ois").Should().NotBeNull();
        registry.GetRequired("USD-sofr-OIS").Should().NotBeNull();
    }

    [Fact]
    public void GetRequired_throws_on_unknown_key()
    {
        var registry = InstrumentConventionRegistry.CreateDefault();

        var act = () => registry.GetRequired("MADEUP-XYZ");

        act.Should().Throw<KeyNotFoundException>()
           .WithMessage("*MADEUP-XYZ*");
    }

    [Fact]
    public void CreateDefault_returns_independent_instances()
    {
        var a = InstrumentConventionRegistry.CreateDefault();
        var b = InstrumentConventionRegistry.CreateDefault();

        a.Should().NotBeSameAs(b);

        b.Add(new FraConvention("X", "USNY", "ACT/360", BusinessDayAdjustment.None, 0));

        var act = () => a.GetRequired("X");
        act.Should().Throw<KeyNotFoundException>();
    }

    [Fact]
    public void Add_null_convention_is_rejected()
    {
        var registry = new InstrumentConventionRegistry();

        var act = () => registry.Add(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
