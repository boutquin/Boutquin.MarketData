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

public sealed class DividendTypeTests
{
    private static readonly IReadOnlyDictionary<string, int> s_expected =
        new Dictionary<string, int>
        {
            ["CanadianEligible"] = 0,
            ["CanadianNonEligible"] = 1,
            ["UsQualified"] = 2,
            ["UsOrdinary"] = 3,
            ["Foreign"] = 4,
            ["ReturnOfCapital"] = 5,
            ["CapitalGainDistribution"] = 6,
            ["Unclassified"] = 7,
        };

    [Fact]
    public void Has_expected_member_count()
    {
        Enum.GetValues<DividendType>().Should().HaveCount(s_expected.Count);
    }

    [Fact]
    public void All_members_match_snapshot_ordinals()
    {
        foreach (var code in Enum.GetValues<DividendType>())
        {
            var name = code.ToString();
            s_expected.Should().ContainKey(name,
                because: "every DividendType member must appear in the pinned snapshot; " +
                         "numeric values must stay bit-compatible with the Trading origin " +
                         "throughout the shim window");
            ((int)code).Should().Be(s_expected[name]);
        }
    }

    [Fact]
    public void No_Unknown_sentinel_exists()
    {
        // Spec §2.2: an `Unknown` sentinel was considered and rejected. Adapters
        // that lack precise classification data must map to the closest category.
        Enum.GetNames<DividendType>().Should().NotContain("Unknown");
    }
}
