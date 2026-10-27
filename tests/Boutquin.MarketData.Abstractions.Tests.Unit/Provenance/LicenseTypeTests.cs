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

public sealed class LicenseTypeTests
{
    [Fact]
    public void MemberSet_IsExactlySixValues_InFixedOrdinalOrder()
    {
        // Pin the append-only contract: reordering or removing members is a breaking change
        // that requires a spec amendment.
        Enum.GetValues<LicenseType>().Should().Equal(
            LicenseType.Free,
            LicenseType.DelayedFifteenMin,
            LicenseType.Commercial,
            LicenseType.ResearchOnly,
            LicenseType.Redistribution,
            LicenseType.Unknown);
    }

    [Theory]
    [InlineData(LicenseType.Free, 0)]
    [InlineData(LicenseType.DelayedFifteenMin, 1)]
    [InlineData(LicenseType.Commercial, 2)]
    [InlineData(LicenseType.ResearchOnly, 3)]
    [InlineData(LicenseType.Redistribution, 4)]
    [InlineData(LicenseType.Unknown, 5)]
    public void Members_HaveStableOrdinalValues(LicenseType value, int expectedOrdinal)
    {
        ((int)value).Should().Be(expectedOrdinal);
    }

    [Fact]
    public void CoversEveryStringFormUsedInProductionSources()
    {
        // Every string form that production code previously used for DataProvenance.LicenseFlag
        // maps to a member in the enum. Adapter authors translate upstream strings to these
        // values explicitly.
        Enum.TryParse<LicenseType>("Free", out _).Should().BeTrue();
        Enum.TryParse<LicenseType>("Commercial", out _).Should().BeTrue();
        Enum.TryParse<LicenseType>("ResearchOnly", out _).Should().BeTrue();
        Enum.TryParse<LicenseType>("DelayedFifteenMin", out _).Should().BeTrue();
        Enum.TryParse<LicenseType>("Redistribution", out _).Should().BeTrue();
        Enum.TryParse<LicenseType>("Unknown", out _).Should().BeTrue();
    }
}
