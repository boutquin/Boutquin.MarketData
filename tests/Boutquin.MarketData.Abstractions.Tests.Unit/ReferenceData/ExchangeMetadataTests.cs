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
/// Exhaustiveness and contract tests for <see cref="ExchangeMetadata"/>
/// (spec feature-reference-taxonomies.md §3.3).
/// </summary>
public sealed class ExchangeMetadataTests
{
    public static IEnumerable<object[]> AllExchangeCodes()
    {
        foreach (var value in Enum.GetValues<ExchangeCode>())
        {
            yield return new object[] { value };
        }
    }

    [Theory]
    [MemberData(nameof(AllExchangeCodes))]
    public void GetTimeZone_covers_every_ExchangeCode_member(ExchangeCode exchange)
    {
        var act = () => ExchangeMetadata.GetTimeZone(exchange);

        act.Should().NotThrow(
            because: "every declared ExchangeCode member must have a timezone mapping; " +
                     "adding a member without the mapping is a spec violation");
    }

    [Theory]
    [MemberData(nameof(AllExchangeCodes))]
    public void GetCountry_covers_every_ExchangeCode_member(ExchangeCode exchange)
    {
        var act = () => ExchangeMetadata.GetCountry(exchange);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(ExchangeCode.XNYS, TimeZoneCode.EST, CountryCode.US)]
    [InlineData(ExchangeCode.XNAS, TimeZoneCode.EST, CountryCode.US)]
    [InlineData(ExchangeCode.XTSE, TimeZoneCode.JST, CountryCode.JP)]
    [InlineData(ExchangeCode.XSHG, TimeZoneCode.CST, CountryCode.CN)]
    [InlineData(ExchangeCode.XHKG, TimeZoneCode.HKT, CountryCode.HK)]
    [InlineData(ExchangeCode.XPAR, TimeZoneCode.CET, CountryCode.FR)]
    [InlineData(ExchangeCode.XLON, TimeZoneCode.GMT, CountryCode.GB)]
    [InlineData(ExchangeCode.XETR, TimeZoneCode.CET, CountryCode.DE)]
    [InlineData(ExchangeCode.XMOS, TimeZoneCode.MSK, CountryCode.RU)]
    [InlineData(ExchangeCode.XTOR, TimeZoneCode.EST, CountryCode.CA)]
    [InlineData(ExchangeCode.NEOE, TimeZoneCode.EST, CountryCode.CA)]
    public void Canonical_mappings_match_spec(ExchangeCode exchange, TimeZoneCode expectedTz, CountryCode expectedCountry)
    {
        ExchangeMetadata.GetTimeZone(exchange).Should().Be(expectedTz);
        ExchangeMetadata.GetCountry(exchange).Should().Be(expectedCountry);
    }

    [Fact]
    public void GetTimeZone_throws_for_unknown_ExchangeCode()
    {
        var synthetic = (ExchangeCode)(-1);

        var act = () => ExchangeMetadata.GetTimeZone(synthetic);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("exchange");
    }

    [Fact]
    public void GetCountry_throws_for_unknown_ExchangeCode()
    {
        var synthetic = (ExchangeCode)(-1);

        var act = () => ExchangeMetadata.GetCountry(synthetic);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("exchange");
    }

    [Fact]
    public void Lookup_returns_value_equal_results_for_same_input()
    {
        // Guards against accidentally exposing a live reference that a caller
        // could mutate — enums are value types so this should be automatic,
        // but the test documents the expected value-semantics guarantee.
        ExchangeMetadata.GetTimeZone(ExchangeCode.XNYS)
            .Should().Be(ExchangeMetadata.GetTimeZone(ExchangeCode.XNYS));
        ExchangeMetadata.GetCountry(ExchangeCode.XNYS)
            .Should().Be(ExchangeMetadata.GetCountry(ExchangeCode.XNYS));
    }
}
