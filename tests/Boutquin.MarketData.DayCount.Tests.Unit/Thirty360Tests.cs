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

public sealed class Thirty360Tests
{
    [Fact]
    public void YearFraction_OneYear_Returns1Point0()
    {
        // 2025-01-01 to 2026-01-01: d1=1, d2=1, years=1, months=0, days=0 => 360/360 = 1.0
        var result = Thirty360.Instance.YearFraction(
            new DateOnly(2025, 1, 1),
            new DateOnly(2026, 1, 1));

        result.Should().BeApproximately(1.0, 1e-12);
    }

    [Fact]
    public void YearFraction_OneMonth_Returns30Over360()
    {
        // 2025-01-01 to 2025-02-01: d1=1, d2=1, years=0, months=1, days=0 => 30/360
        var result = Thirty360.Instance.YearFraction(
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 2, 1));

        result.Should().BeApproximately(30.0 / 360.0, 1e-12);
    }

    [Fact]
    public void YearFraction_SameDate_ReturnsZero()
    {
        var result = Thirty360.Instance.YearFraction(
            new DateOnly(2025, 6, 15),
            new DateOnly(2025, 6, 15));

        result.Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void YearFraction_Feb28ToMar1NonLeap_Returns3Over360()
    {
        // 2025-02-28 to 2025-03-01: d1=28, d2=1, years=0, months=1, days=1-28=-27
        // => (30 + (-27)) / 360 = 3/360
        var result = Thirty360.Instance.YearFraction(
            new DateOnly(2025, 2, 28),
            new DateOnly(2025, 3, 1));

        result.Should().BeApproximately(3.0 / 360.0, 1e-12);
    }

    [Fact]
    public void YearFraction_Feb29ToMar1Leap_Returns2Over360()
    {
        // 2024-02-29 to 2024-03-01: d1=29, d2=1, years=0, months=1, days=1-29=-28
        // => (30 + (-28)) / 360 = 2/360
        var result = Thirty360.Instance.YearFraction(
            new DateOnly(2024, 2, 29),
            new DateOnly(2024, 3, 1));

        result.Should().BeApproximately(2.0 / 360.0, 1e-12);
    }

    [Fact]
    public void YearFraction_MonthEnd31st_CapsStartDayTo30()
    {
        // 2025-01-31 to 2025-02-15: d1=min(31,30)=30, d2=15, months=1, days=15-30=-15
        // => (30 + (-15)) / 360 = 15/360
        var result = Thirty360.Instance.YearFraction(
            new DateOnly(2025, 1, 31),
            new DateOnly(2025, 2, 15));

        result.Should().BeApproximately(15.0 / 360.0, 1e-12);
    }

    [Fact]
    public void YearFraction_BothMonthsEnd31st_Returns60Over360()
    {
        // 2025-01-31 to 2025-03-31: d1=30, d2=30, months=2, days=0 => 60/360 = 1/6
        var result = Thirty360.Instance.YearFraction(
            new DateOnly(2025, 1, 31),
            new DateOnly(2025, 3, 31));

        result.Should().BeApproximately(60.0 / 360.0, 1e-12);
    }

    [Fact]
    public void Code_Returns30360()
    {
        Thirty360.Instance.Code.Should().Be("30/360");
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        var a = Thirty360.Instance;
        var b = Thirty360.Instance;

        a.Should().BeSameAs(b);
    }

    [Fact]
    public void YearFraction_BondBasisDistinctiveCase_DiffersFrom30E360()
    {
        // ISDA Bond Basis vs 30E/360 disagree when D2 = 31 and D1 < 30 (uncapped).
        // 2026-01-15 → 2026-07-31:
        //   Bond Basis: d1 = min(15, 30) = 15; d1 != 30 so D2 stays 31.
        //     yf = (360·0 + 30·6 + (31 − 15)) / 360 = (180 + 16) / 360 = 196 / 360
        //   30E/360: d2 = min(31, 30) = 30 unconditionally.
        //     yf = (30·6 + (30 − 15)) / 360 = 195 / 360
        // This test locks in that Thirty360 implements Bond Basis, not 30E/360.
        var bondBasis = Thirty360.Instance.YearFraction(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 7, 31));
        var eurobond = ThirtyE360.Instance.YearFraction(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 7, 31));

        bondBasis.Should().BeApproximately(196.0 / 360.0, 1e-12);
        eurobond.Should().BeApproximately(195.0 / 360.0, 1e-12);
        bondBasis.Should().NotBe(eurobond);
    }

    [Fact]
    public void YearFraction_BondBasisCapsD2_WhenD1AlsoCaps()
    {
        // 2026-01-31 → 2026-07-31: d1 = min(31, 30) = 30; D2 = 31 with d1 == 30 → d2 = 30.
        // yf = (30·6 + (30 − 30)) / 360 = 180 / 360 = 0.5
        Thirty360.Instance.YearFraction(
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 7, 31))
            .Should().BeApproximately(0.5, 1e-12);
    }

    [Fact]
    public void YearFraction_InvertedRange_Throws()
    {
        var act = () => Thirty360.Instance.YearFraction(
            new DateOnly(2026, 7, 31),
            new DateOnly(2026, 1, 15));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("endDate");
    }
}
