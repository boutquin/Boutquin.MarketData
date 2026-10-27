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

using Boutquin.Domain.Enumerations;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.ReferenceData;

/// <summary>
/// Locks down the ISO 4217 numeric values of the <see cref="Currency"/> members that
/// MarketData's persisted records depend on (currency-unification milestone M-1).
/// </summary>
/// <remarks>
/// <para>
/// MarketData formerly owned a 14-member <c>CurrencyCode</c> enum; the unification
/// onto <see cref="Boutquin.Domain.Enumerations.Currency"/> deleted it. The serialized
/// wire format of <c>FxRateDatasetKey</c> and <c>DividendPayment</c> encodes the
/// currency column as its ISO 4217 numeric value, so those numbers are a wire-format
/// contract — this snapshot pins them so a change to the upstream <see cref="Currency"/>
/// numeric backing (which would silently corrupt persisted data) fails the build here.
/// </para>
/// <para>
/// Only the subset of <see cref="Currency"/> members reachable through MarketData's
/// records is pinned; <see cref="Currency"/> itself is a 177-member ISO catalogue owned
/// and exhaustively tested by Boutquin.Domain.
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
    public void Expected_members_exist_in_Currency()
    {
        foreach (var name in s_expected.Keys)
        {
            Enum.IsDefined(typeof(Currency), name)
                .Should().BeTrue(because: $"MarketData depends on Currency.{name}; " +
                                          "the unification must not drop a member MarketData persists");
        }
    }

    [Theory]
    [InlineData(Currency.USD, 840)]
    [InlineData(Currency.CAD, 124)]
    [InlineData(Currency.MXN, 484)]
    [InlineData(Currency.GBP, 826)]
    [InlineData(Currency.EUR, 978)]
    [InlineData(Currency.JPY, 392)]
    [InlineData(Currency.CNY, 156)]
    [InlineData(Currency.INR, 356)]
    [InlineData(Currency.AUD, 36)]
    [InlineData(Currency.BRL, 986)]
    [InlineData(Currency.RUB, 643)]
    [InlineData(Currency.KRW, 410)]
    [InlineData(Currency.HKD, 344)]
    [InlineData(Currency.CHF, 756)]
    public void ISO_4217_numeric_values_are_pinned(Currency code, int expected)
    {
        ((int)code).Should().Be(expected);
    }

    [Fact]
    public void All_snapshot_members_map_to_Currency_with_pinned_value()
    {
        foreach (var (name, expected) in s_expected)
        {
            var code = Enum.Parse<Currency>(name);
            ((int)code).Should().Be(expected,
                because: $"the serialized wire format encodes Currency.{name} as its ISO 4217 " +
                         "numeric value; this number is a persisted-data contract");
        }
    }
}
