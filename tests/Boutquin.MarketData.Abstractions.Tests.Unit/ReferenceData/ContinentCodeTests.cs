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

public sealed class ContinentCodeTests
{
    private static readonly IReadOnlyDictionary<string, (int Ordinal, string Description)> s_expected =
        new Dictionary<string, (int, string)>
        {
            ["AF"] = (0, "Africa"),
            ["AN"] = (1, "Antarctica"),
            ["AS"] = (2, "Asia"),
            ["EU"] = (3, "Europe"),
            ["NA"] = (4, "North America"),
            ["OC"] = (5, "Oceania"),
            ["SA"] = (6, "South America"),
        };

    [Fact]
    public void Has_expected_member_count()
    {
        Enum.GetValues<ContinentCode>().Should().HaveCount(s_expected.Count);
    }

    [Fact]
    public void All_members_match_snapshot_ordinals()
    {
        foreach (var code in Enum.GetValues<ContinentCode>())
        {
            var name = code.ToString();
            s_expected.Should().ContainKey(name);
            ((int)code).Should().Be(s_expected[name].Ordinal);
        }
    }

    [Fact]
    public void All_members_carry_expected_Description_attribute()
    {
        foreach (var code in Enum.GetValues<ContinentCode>())
        {
            var name = code.ToString();
            var description = typeof(ContinentCode)
                .GetField(name)!
                .GetCustomAttribute<DescriptionAttribute>()
                ?.Description;

            description.Should().Be(s_expected[name].Description);
        }
    }
}
