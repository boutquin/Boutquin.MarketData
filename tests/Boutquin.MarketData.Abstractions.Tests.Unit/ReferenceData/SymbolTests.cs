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

public sealed class SymbolTests
{
    [Fact]
    public void Construction_preserves_ticker()
    {
        new Symbol("AAPL").Ticker.Should().Be("AAPL");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Construction_rejects_null_or_whitespace(string? ticker)
    {
        var act = () => _ = new Symbol(ticker!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_returns_ticker()
    {
        new Symbol("MSFT").ToString().Should().Be("MSFT");
    }

    [Fact]
    public void Equality_is_structural()
    {
        var a = new Symbol("AAPL");
        var b = new Symbol("AAPL");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Case_is_preserved()
    {
        var upper = new Symbol("AAPL");
        var lower = new Symbol("aapl");

        upper.Should().NotBe(lower);
    }

    [Fact]
    public void CompareTo_uses_ordinal_ordering()
    {
        var a = new Symbol("AAPL");
        var m = new Symbol("MSFT");

        a.CompareTo(m).Should().BeLessThan(0);
        m.CompareTo(a).Should().BeGreaterThan(0);
        a.CompareTo(a).Should().Be(0);
    }

    [Fact]
    public void Can_be_used_as_dictionary_key()
    {
        var dict = new Dictionary<Symbol, int>
        {
            [new Symbol("AAPL")] = 1,
            [new Symbol("MSFT")] = 2,
        };

        dict[new Symbol("AAPL")].Should().Be(1);
    }
}
