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

/// <summary>
/// Shared fixture covering the five external-ID record structs that follow the
/// <see cref="Symbol"/> pattern — thin wrappers around <see cref="string"/> with
/// null/whitespace rejection.
/// </summary>
public sealed class ExternalIdTests
{
    [Fact]
    public void YieldCurveId_RejectsNullOrWhitespace()
    {
        AssertRejectsNullOrWhitespace(s => new YieldCurveId(s));
    }

    [Fact]
    public void YieldCurveId_PreservesValue()
    {
        new YieldCurveId("CAD-ZERO").Value.Should().Be("CAD-ZERO");
        new YieldCurveId("CAD-ZERO").ToString().Should().Be("CAD-ZERO");
    }

    [Fact]
    public void YieldCurveId_Equality_And_Ordering()
    {
        new YieldCurveId("A").Should().Be(new YieldCurveId("A"));
        new YieldCurveId("A").CompareTo(new YieldCurveId("B")).Should().BeNegative();
    }

    [Fact]
    public void EconomicSeriesId_RejectsNullOrWhitespace()
    {
        AssertRejectsNullOrWhitespace(s => new EconomicSeriesId(s));
    }

    [Fact]
    public void EconomicSeriesId_PreservesValue()
    {
        new EconomicSeriesId("DGS10").Value.Should().Be("DGS10");
    }

    [Fact]
    public void EconomicSeriesId_CaseSensitive()
    {
        new EconomicSeriesId("dgs10").Should().NotBe(new EconomicSeriesId("DGS10"));
    }

    [Fact]
    public void FactorDatasetId_RejectsNullOrWhitespace()
    {
        AssertRejectsNullOrWhitespace(s => new FactorDatasetId(s));
    }

    [Fact]
    public void FactorDatasetId_PreservesDotsAndUnderscoresAndHyphens()
    {
        new FactorDatasetId("F-F_Research_Data_Factors_daily")
            .Value.Should().Be("F-F_Research_Data_Factors_daily");
    }

    [Fact]
    public void FuturesProductCode_RejectsNullOrWhitespace()
    {
        AssertRejectsNullOrWhitespace(s => new FuturesProductCode(s));
    }

    [Fact]
    public void FuturesProductCode_PreservesCase()
    {
        new FuturesProductCode("SR3").Value.Should().Be("SR3");
        new FuturesProductCode("sr3").Value.Should().Be("sr3");
    }

    [Fact]
    public void DefaultInstance_ToString_IsEmpty()
    {
        default(YieldCurveId).ToString().Should().Be(string.Empty);
        default(EconomicSeriesId).ToString().Should().Be(string.Empty);
        default(FactorDatasetId).ToString().Should().Be(string.Empty);
        default(FuturesProductCode).ToString().Should().Be(string.Empty);
    }

    private static void AssertRejectsNullOrWhitespace(Action<string> ctor)
    {
        ((Action)(() => ctor(null!))).Should().Throw<ArgumentException>();
        ((Action)(() => ctor(""))).Should().Throw<ArgumentException>();
        ((Action)(() => ctor("   "))).Should().Throw<ArgumentException>();
    }
}
