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

using Boutquin.MarketData.Abstractions.Provenance;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Provenance;

public sealed class ProviderCodeTests
{
    [Theory]
    [InlineData("tiingo", "tiingo")]
    [InlineData("TIINGO", "tiingo")]
    [InlineData("Tiingo", "tiingo")]
    [InlineData("tIIngO", "tiingo")]
    public void Constructor_NormalizesToLowerCase(string input, string expected)
    {
        new ProviderCode(input).Value.Should().Be(expected);
    }

    [Fact]
    public void Constructor_PreservesInternalHyphensAndDigits()
    {
        new ProviderCode("Ny-Fed-2").Value.Should().Be("ny-fed-2");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_RejectsNullOrWhitespace(string? input)
    {
        Action act = () => _ = new ProviderCode(input!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equality_IsCaseInsensitiveViaNormalization()
    {
        new ProviderCode("Tiingo").Should().Be(new ProviderCode("TIINGO"));
        new ProviderCode("Tiingo").GetHashCode().Should().Be(new ProviderCode("TIINGO").GetHashCode());
    }

    [Fact]
    public void Inequality_IsRecognized()
    {
        new ProviderCode("tiingo").Should().NotBe(new ProviderCode("fred"));
    }

    [Fact]
    public void ToString_ReturnsCanonicalValue()
    {
        new ProviderCode("Tiingo").ToString().Should().Be("tiingo");
    }

    [Fact]
    public void ToString_OnDefault_ReturnsEmpty()
    {
        default(ProviderCode).ToString().Should().Be(string.Empty);
    }

    [Fact]
    public void CompareTo_UsesOrdinalOnCanonicalForm()
    {
        new ProviderCode("alpha").CompareTo(new ProviderCode("BETA")).Should().BeNegative();
        new ProviderCode("gamma").CompareTo(new ProviderCode("gamma")).Should().Be(0);
    }

    [Fact]
    public void SortedSet_OrderingIsDeterministic()
    {
        var set = new SortedSet<ProviderCode>
        {
            new("FRED"),
            new("boc"),
            new("Tiingo"),
        };

        set.Select(c => c.Value).Should().Equal("boc", "fred", "tiingo");
    }
}
