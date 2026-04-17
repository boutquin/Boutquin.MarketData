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

using System.ComponentModel;
using System.Reflection;

using Boutquin.MarketData.Abstractions.ReferenceData;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.ReferenceData;

/// <summary>
/// Round-trip snapshot for <see cref="CountryCode"/> relative to its Trading origin
/// (see spec feature-reference-taxonomies.md §3.1).
/// </summary>
/// <remarks>
/// <para>
/// Following the Tier 1 <c>CurrencyCodeTests</c> pattern, the expected
/// (member → ordinal → description) triples are hard-coded here rather than
/// imported from Trading — the architecture rule "MarketData must not depend
/// on Trading" forbids the latter. Any drift from the Trading-side snapshot
/// must be fixed in both repos in the same release cycle (spec §6 decision rule).
/// </para>
/// </remarks>
public sealed class CountryCodeTests
{
    private static readonly IReadOnlyDictionary<string, (int Ordinal, string Description)> s_expected =
        new Dictionary<string, (int, string)>
        {
            ["CA"] = (0, "Canada"),
            ["CN"] = (1, "China"),
            ["FR"] = (2, "France"),
            ["DE"] = (3, "Germany"),
            ["HK"] = (4, "Hong Kong"),
            ["IN"] = (5, "India"),
            ["JP"] = (6, "Japan"),
            ["RU"] = (7, "Russia"),
            ["KR"] = (8, "South Korea"),
            ["GB"] = (9, "United Kingdom"),
            ["US"] = (10, "United States"),
        };

    [Fact]
    public void Has_expected_member_count()
    {
        Enum.GetValues<CountryCode>().Should().HaveCount(s_expected.Count);
    }

    [Fact]
    public void All_members_match_snapshot_ordinals()
    {
        foreach (var code in Enum.GetValues<CountryCode>())
        {
            var name = code.ToString();
            s_expected.Should().ContainKey(name,
                because: "every CountryCode member must appear in the pinned snapshot; " +
                         "adding a new member requires updating this snapshot and the " +
                         "Trading-side copy in the same release");
            ((int)code).Should().Be(s_expected[name].Ordinal);
        }
    }

    [Fact]
    public void All_members_carry_expected_Description_attribute()
    {
        foreach (var code in Enum.GetValues<CountryCode>())
        {
            var name = code.ToString();
            var description = typeof(CountryCode)
                .GetField(name)!
                .GetCustomAttribute<DescriptionAttribute>()
                ?.Description;

            description.Should().Be(s_expected[name].Description,
                because: "Description attributes are consumed by display/serialisation and " +
                         "must not drift from the Trading origin");
        }
    }
}
