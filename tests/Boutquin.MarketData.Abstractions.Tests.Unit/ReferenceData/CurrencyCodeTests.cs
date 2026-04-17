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
/// Locks down the ISO 4217 numeric values assigned to <see cref="CurrencyCode"/>.
/// </summary>
/// <remarks>
/// <para>
/// The spec requires the MarketData enum to stay bit-compatible with
/// <c>Boutquin.Trading.Domain.Enums.CurrencyCode</c> during the shim window
/// (§3.3 round-trip AC, §6 drift rule). Adding a MarketData dependency on
/// Trading in this test would violate the architecture rule "MarketData
/// must not depend on Trading" — so the expected (member, numeric) pairs
/// are hard-coded here as a trust boundary.
/// </para>
/// <para>
/// Updating this snapshot requires a matching update to
/// <c>Boutquin.Trading.Domain.Enums.CurrencyCode</c> in the same release
/// cycle, per spec §6 "drift is not a supported state".
/// </para>
/// </remarks>
public sealed class CurrencyCodeTests
{
    private static readonly IReadOnlyDictionary<string, int> s_expected =
        new Dictionary<string, int>
        {
            ["USD"] = 840,
            ["CAD"] = 124,
            ["MXN"] = 484,
            ["GBP"] = 826,
            ["EUR"] = 978,
            ["JPY"] = 392,
            ["CNY"] = 156,
            ["INR"] = 356,
            ["AUD"] = 36,
            ["BRL"] = 986,
            ["RUB"] = 643,
            ["KRW"] = 410,
            ["HKD"] = 344,
            ["CHF"] = 756,
        };

    [Fact]
    public void Has_expected_member_count()
    {
        Enum.GetValues<CurrencyCode>().Should().HaveCount(s_expected.Count);
    }

    [Theory]
    [InlineData(CurrencyCode.USD, 840)]
    [InlineData(CurrencyCode.CAD, 124)]
    [InlineData(CurrencyCode.MXN, 484)]
    [InlineData(CurrencyCode.GBP, 826)]
    [InlineData(CurrencyCode.EUR, 978)]
    [InlineData(CurrencyCode.JPY, 392)]
    [InlineData(CurrencyCode.CNY, 156)]
    [InlineData(CurrencyCode.INR, 356)]
    [InlineData(CurrencyCode.AUD, 36)]
    [InlineData(CurrencyCode.BRL, 986)]
    [InlineData(CurrencyCode.RUB, 643)]
    [InlineData(CurrencyCode.KRW, 410)]
    [InlineData(CurrencyCode.HKD, 344)]
    [InlineData(CurrencyCode.CHF, 756)]
    public void ISO_4217_numeric_values_are_pinned(CurrencyCode code, int expected)
    {
        ((int)code).Should().Be(expected);
    }

    [Fact]
    public void All_members_match_snapshot()
    {
        foreach (var code in Enum.GetValues<CurrencyCode>())
        {
            s_expected.Should().ContainKey(code.ToString(),
                because: "every CurrencyCode member must appear in the pinned snapshot; " +
                         "adding a new member requires updating both this snapshot and " +
                         "the Trading-side shim in the same release");
            ((int)code).Should().Be(s_expected[code.ToString()]);
        }
    }
}
