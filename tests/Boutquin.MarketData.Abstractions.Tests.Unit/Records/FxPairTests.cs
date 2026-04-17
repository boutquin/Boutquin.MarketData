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

using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

public sealed class FxPairTests
{
    [Fact]
    public void ToString_ReturnsBaseSlashQuote()
    {
        var pair = new FxPair(CurrencyCode.EUR, CurrencyCode.USD);

        pair.ToString().Should().Be("EUR/USD");
    }

    [Fact]
    public void Invert_SwapsBaseAndQuote()
    {
        var pair = new FxPair(CurrencyCode.EUR, CurrencyCode.USD);

        var inverted = pair.Invert();

        inverted.BaseCurrency.Should().Be(CurrencyCode.USD);
        inverted.QuoteCurrency.Should().Be(CurrencyCode.EUR);
    }

    [Fact]
    public void MarketConvention_EurAlwaysBase()
    {
        var pair = new FxPair(CurrencyCode.USD, CurrencyCode.EUR);

        pair.MarketConvention().Should().Be(new FxPair(CurrencyCode.EUR, CurrencyCode.USD));
    }

    [Fact]
    public void StandardSettlementDays_SameCurrency_T0()
    {
        new FxPair(CurrencyCode.USD, CurrencyCode.USD).StandardSettlementDays.Should().Be(0);
    }

    [Fact]
    public void StandardSettlementDays_UsdCad_T1()
    {
        new FxPair(CurrencyCode.USD, CurrencyCode.CAD).StandardSettlementDays.Should().Be(1);
    }

    [Fact]
    public void StandardSettlementDays_CadUsd_T1()
    {
        // Reversed order should still resolve to USD/CAD convention.
        new FxPair(CurrencyCode.CAD, CurrencyCode.USD).StandardSettlementDays.Should().Be(1);
    }

    [Fact]
    public void StandardSettlementDays_Default_T2()
    {
        new FxPair(CurrencyCode.EUR, CurrencyCode.USD).StandardSettlementDays.Should().Be(2);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var pair1 = new FxPair(CurrencyCode.GBP, CurrencyCode.JPY);
        var pair2 = new FxPair(CurrencyCode.GBP, CurrencyCode.JPY);

        pair1.Should().Be(pair2);
    }
}
