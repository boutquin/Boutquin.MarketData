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

public sealed class CountryMetadataTests
{
    public static IEnumerable<object[]> AllCountryCodes()
    {
        foreach (var value in Enum.GetValues<CountryCode>())
        {
            yield return new object[] { value };
        }
    }

    [Theory]
    [MemberData(nameof(AllCountryCodes))]
    public void GetContinent_covers_every_CountryCode_member(CountryCode country)
    {
        var act = () => CountryMetadata.GetContinent(country);

        act.Should().NotThrow(
            because: "every declared CountryCode member must have a continent mapping");
    }

    [Theory]
    [InlineData(CountryCode.CA, ContinentCode.NA)]
    [InlineData(CountryCode.US, ContinentCode.NA)]
    [InlineData(CountryCode.CN, ContinentCode.AS)]
    [InlineData(CountryCode.FR, ContinentCode.EU)]
    [InlineData(CountryCode.DE, ContinentCode.EU)]
    [InlineData(CountryCode.HK, ContinentCode.AS)]
    [InlineData(CountryCode.IN, ContinentCode.AS)]
    [InlineData(CountryCode.JP, ContinentCode.AS)]
    [InlineData(CountryCode.KR, ContinentCode.AS)]
    [InlineData(CountryCode.GB, ContinentCode.EU)]
    [InlineData(CountryCode.RU, ContinentCode.EU)]
    public void Canonical_mappings_match_spec(CountryCode country, ContinentCode expected)
    {
        CountryMetadata.GetContinent(country).Should().Be(expected);
    }

    [Fact]
    public void GetContinent_throws_for_unknown_CountryCode()
    {
        var synthetic = (CountryCode)(-1);

        var act = () => CountryMetadata.GetContinent(synthetic);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("country");
    }
}
