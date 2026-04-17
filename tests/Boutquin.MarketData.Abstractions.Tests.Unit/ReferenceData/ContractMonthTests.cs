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

public sealed class ContractMonthTests
{
    [Fact]
    public void IntIntConstructor_StoresYearAndMonth()
    {
        var cm = new ContractMonth(2026, 12);
        cm.Year.Should().Be(2026);
        cm.Month.Should().Be(12);
    }

    [Theory]
    [InlineData("2026-12", 2026, 12)]
    [InlineData("2026-01", 2026, 1)]
    [InlineData("202612", 2026, 12)]
    [InlineData("202601", 2026, 1)]
    [InlineData("  2026-06  ", 2026, 6)]
    public void StringConstructor_AcceptsCanonicalAndCompactForms(string input, int expectedYear, int expectedMonth)
    {
        var cm = new ContractMonth(input);
        cm.Year.Should().Be(expectedYear);
        cm.Month.Should().Be(expectedMonth);
    }

    [Theory]
    [InlineData("2026/12")]
    [InlineData("2026-12-01")]
    [InlineData("26-12")]
    [InlineData("abcdef")]
    [InlineData("2026-ab")]
    public void StringConstructor_RejectsMalformedInput(string input)
    {
        Action act = () => _ = new ContractMonth(input);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StringConstructor_RejectsNullOrWhitespace(string input)
    {
        Action act = () => _ = new ContractMonth(input);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StringConstructor_RejectsNull()
    {
        Action act = () => _ = new ContractMonth(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    [InlineData(2026, -1)]
    public void IntIntConstructor_RejectsInvalidMonth(int year, int month)
    {
        Action act = () => _ = new ContractMonth(year, month);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(10000, 1)]
    public void IntIntConstructor_RejectsInvalidYear(int year, int month)
    {
        Action act = () => _ = new ContractMonth(year, month);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    public void StringConstructor_RejectsInvalidMonth(string input)
    {
        Action act = () => _ = new ContractMonth(input);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToString_RendersCanonicalForm()
    {
        new ContractMonth(2026, 1).ToString().Should().Be("2026-01");
        new ContractMonth(2026, 12).ToString().Should().Be("2026-12");
        new ContractMonth("202606").ToString().Should().Be("2026-06");
    }

    [Fact]
    public void CompareTo_OrdersByYearThenMonth()
    {
        new ContractMonth(2025, 12).CompareTo(new ContractMonth(2026, 1)).Should().BeNegative();
        new ContractMonth(2026, 3).CompareTo(new ContractMonth(2026, 1)).Should().BePositive();
        new ContractMonth(2026, 6).CompareTo(new ContractMonth(2026, 6)).Should().Be(0);
    }

    [Fact]
    public void SortedSet_SortsChronologically()
    {
        var set = new SortedSet<ContractMonth>
        {
            new(2026, 6),
            new(2025, 12),
            new(2026, 1),
        };

        set.Select(c => c.ToString()).Should().Equal("2025-12", "2026-01", "2026-06");
    }

    [Fact]
    public void ImplicitConversionToDateOnly_IsFirstOfMonth()
    {
        DateOnly firstDay = new ContractMonth(2026, 6);
        firstDay.Should().Be(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void ToFirstOfMonth_Equivalent()
    {
        new ContractMonth(2026, 6).ToFirstOfMonth().Should().Be(new DateOnly(2026, 6, 1));
    }

    [Fact]
    public void Equality_Works()
    {
        new ContractMonth(2026, 6).Should().Be(new ContractMonth("2026-06"));
        new ContractMonth(2026, 6).GetHashCode()
            .Should().Be(new ContractMonth(2026, 6).GetHashCode());
    }
}
