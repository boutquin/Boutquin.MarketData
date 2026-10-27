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
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Normalization.Normalizers;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Normalization.Tests.Unit;

public sealed class DividendPaymentNormalizerTests
{
    private readonly DividendPaymentNormalizer _sut = new();

    [Fact]
    public void Default_record_is_valid_under_required_string_policy()
    {
        var result = _sut.Normalize(default);

        result.Should().Be(default(DividendPayment));
    }

    public static IEnumerable<object[]> AllDividendTypes =>
        Enum.GetValues<DividendType>().Select(type => new object[] { type });

    [Theory]
    [InlineData(-1)]
    [InlineData(29)]
    public void Constructor_rejects_precision_outside_decimal_rounding_range(int decimalPlaces)
    {
        var construct = () => new DividendPaymentNormalizer(decimalPlaces);

        construct.Should().Throw<ArgumentOutOfRangeException>()
            .And.ParamName.Should().Be("decimalPlaces");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(28)]
    public void Constructor_accepts_decimal_rounding_boundaries(int decimalPlaces)
    {
        var construct = () => new DividendPaymentNormalizer(decimalPlaces);

        construct.Should().NotThrow();
    }

    [Fact]
    public void Preserves_currency_code()
    {
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, Currency.USD, DividendType.UsOrdinary);

        var result = _sut.Normalize(dp);

        result.Currency.Should().Be(Currency.USD);
    }

    [Fact]
    public void Clamps_negative_amount_to_zero()
    {
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), -0.50m, Currency.USD, DividendType.UsOrdinary);

        var result = _sut.Normalize(dp);

        result.Amount.Should().Be(0m);
    }

    [Fact]
    public void Rounds_amount_to_configured_precision()
    {
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), 0.8233337m, Currency.USD, DividendType.UsOrdinary);

        var result = _sut.Normalize(dp);

        result.Amount.Should().Be(0.8233m);
    }

    [Fact]
    public void Preserves_valid_payment()
    {
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, Currency.USD, DividendType.UsOrdinary);

        var result = _sut.Normalize(dp);

        result.ExDate.Should().Be(new DateOnly(2024, 8, 15));
        result.Amount.Should().Be(0.82m);
        result.Currency.Should().Be(Currency.USD);
    }

    [Fact]
    public void Zero_amount_preserved()
    {
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), 0m, Currency.USD, DividendType.UsOrdinary);

        var result = _sut.Normalize(dp);

        result.Amount.Should().Be(0m);
    }

    [Theory]
    [MemberData(nameof(AllDividendTypes))]
    public void Type_is_passed_through_unchanged(DividendType type)
    {
        // Classification is an issuer-declared fact, not a unit or format concern —
        // the normalizer must not alter it.
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, Currency.USD, type);

        var result = _sut.Normalize(dp);

        result.Type.Should().Be(type);
    }

    [Fact]
    public void Type_pass_through_cases_are_exhaustive()
    {
        AllDividendTypes
            .Select(row => (DividendType)row[0])
            .Should().Equal(Enum.GetValues<DividendType>());
    }
}
