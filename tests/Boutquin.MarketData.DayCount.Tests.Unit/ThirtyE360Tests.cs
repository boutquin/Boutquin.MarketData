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

public sealed class ThirtyE360Tests
{
    [Fact]
    public void Code_is_30E_360()
    {
        ThirtyE360.Instance.Code.Should().Be("30E/360");
    }

    [Fact]
    public void Half_year_exact_months_returns_half()
    {
        // (360·0 + 30·6 + 0) / 360 = 0.5
        ThirtyE360.Instance.YearFraction(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 7, 15)).Should().Be(0.5);
    }

    [Fact]
    public void Jan31_to_Feb28_has_no_EOM_adjustment()
    {
        // d1 = min(31, 30) = 30; d2 = min(28, 30) = 28
        // yf = (360·0 + 30·1 + (28 − 30)) / 360 = 28 / 360
        var yf = ThirtyE360.Instance.YearFraction(
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 2, 28));

        yf.Should().BeApproximately(28.0 / 360.0, 1e-12);
    }

    [Fact]
    public void Caps_D1_at_30()
    {
        // d1 = min(31, 30) = 30; d2 = min(15, 30) = 15
        // yf = (360·0 + 30·1 + (15 − 30)) / 360 = 15 / 360
        var yf = ThirtyE360.Instance.YearFraction(
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 2, 15));

        yf.Should().BeApproximately(15.0 / 360.0, 1e-12);
    }

    [Fact]
    public void Caps_D2_at_30()
    {
        // d1 = min(15, 30) = 15; d2 = min(31, 30) = 30
        // yf = (360·0 + 30·1 + (30 − 15)) / 360 = 45 / 360 = 0.125
        var yf = ThirtyE360.Instance.YearFraction(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 2, 28));

        // (30·1 + (28 − 15)) / 360
        yf.Should().BeApproximately((30 + 13) / 360.0, 1e-12);
    }

    [Fact]
    public void Full_year_exact_returns_one()
    {
        ThirtyE360.Instance.YearFraction(
            new DateOnly(2026, 1, 15),
            new DateOnly(2027, 1, 15)).Should().Be(1.0);
    }

    [Fact]
    public void Same_day_returns_zero()
    {
        ThirtyE360.Instance.YearFraction(
            new DateOnly(2026, 6, 15),
            new DateOnly(2026, 6, 15)).Should().Be(0.0);
    }
}
