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

using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

/// <summary>
/// Covers the 4-parameter <see cref="DividendPayment"/> constructor added by
/// the Tier 2 reference-taxonomies spec (§3.2).
/// </summary>
public sealed class DividendPaymentTests
{
    [Fact]
    public void Construction_preserves_all_fields_including_Type()
    {
        var dp = new DividendPayment(
            new DateOnly(2024, 8, 15),
            0.82m,
            CurrencyCode.USD,
            DividendType.UsQualified);

        dp.ExDate.Should().Be(new DateOnly(2024, 8, 15));
        dp.Amount.Should().Be(0.82m);
        dp.Currency.Should().Be(CurrencyCode.USD);
        dp.Type.Should().Be(DividendType.UsQualified);
    }

    [Fact]
    public void Equality_requires_matching_Type()
    {
        var a = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, CurrencyCode.USD, DividendType.UsQualified);
        var b = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, CurrencyCode.USD, DividendType.UsQualified);

        a.Should().Be(b);
    }

    [Fact]
    public void Different_Type_breaks_equality()
    {
        // Classification matters for portfolio-income aggregation: two payments
        // with the same ex-date, amount, and currency are *not* the same economic
        // event if the jurisdictional category differs.
        var qualified = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, CurrencyCode.USD, DividendType.UsQualified);
        var ordinary = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, CurrencyCode.USD, DividendType.UsOrdinary);

        qualified.Should().NotBe(ordinary);
    }

    [Fact]
    public void Different_currency_is_not_equal()
    {
        var a = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, CurrencyCode.USD, DividendType.UsOrdinary);
        var b = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, CurrencyCode.GBP, DividendType.Foreign);

        a.Should().NotBe(b);
    }

    [Theory]
    [InlineData(DividendType.CanadianEligible)]
    [InlineData(DividendType.CanadianNonEligible)]
    [InlineData(DividendType.UsQualified)]
    [InlineData(DividendType.UsOrdinary)]
    [InlineData(DividendType.Foreign)]
    [InlineData(DividendType.ReturnOfCapital)]
    [InlineData(DividendType.CapitalGainDistribution)]
    [InlineData(DividendType.Unclassified)]
    public void Every_DividendType_member_is_accepted(DividendType type)
    {
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), 1m, CurrencyCode.USD, type);

        dp.Type.Should().Be(type);
    }

    [Fact]
    public void With_expression_can_change_Type_leaving_other_fields_intact()
    {
        // Supports the adapter-side "classify after construction" workflow where
        // a provider emits the payment first and a classifier enriches it later.
        var raw = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, CurrencyCode.USD, DividendType.Unclassified);

        var classified = raw with { Type = DividendType.UsQualified };

        classified.ExDate.Should().Be(raw.ExDate);
        classified.Amount.Should().Be(raw.Amount);
        classified.Currency.Should().Be(raw.Currency);
        classified.Type.Should().Be(DividendType.UsQualified);
    }
}
