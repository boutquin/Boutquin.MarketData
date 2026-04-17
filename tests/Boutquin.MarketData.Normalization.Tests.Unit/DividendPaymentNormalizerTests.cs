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
using Boutquin.MarketData.Normalization.Normalizers;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Normalization.Tests.Unit;

public sealed class DividendPaymentNormalizerTests
{
    private readonly DividendPaymentNormalizer _sut = new();

    [Fact]
    public void Preserves_currency_code()
    {
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, CurrencyCode.USD, DividendType.UsOrdinary);

        var result = _sut.Normalize(dp);

        result.Currency.Should().Be(CurrencyCode.USD);
    }

    [Fact]
    public void Clamps_negative_amount_to_zero()
    {
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), -0.50m, CurrencyCode.USD, DividendType.UsOrdinary);

        var result = _sut.Normalize(dp);

        result.Amount.Should().Be(0m);
    }

    [Fact]
    public void Rounds_amount_to_configured_precision()
    {
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), 0.8233337m, CurrencyCode.USD, DividendType.UsOrdinary);

        var result = _sut.Normalize(dp);

        result.Amount.Should().Be(0.8233m);
    }

    [Fact]
    public void Preserves_valid_payment()
    {
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, CurrencyCode.USD, DividendType.UsOrdinary);

        var result = _sut.Normalize(dp);

        result.ExDate.Should().Be(new DateOnly(2024, 8, 15));
        result.Amount.Should().Be(0.82m);
        result.Currency.Should().Be(CurrencyCode.USD);
    }

    [Fact]
    public void Zero_amount_preserved()
    {
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), 0m, CurrencyCode.USD, DividendType.UsOrdinary);

        var result = _sut.Normalize(dp);

        result.Amount.Should().Be(0m);
    }

    [Theory]
    [InlineData(DividendType.UsQualified)]
    [InlineData(DividendType.CanadianEligible)]
    [InlineData(DividendType.Foreign)]
    [InlineData(DividendType.ReturnOfCapital)]
    public void Type_is_passed_through_unchanged(DividendType type)
    {
        // Classification is an issuer-declared fact, not a unit or format concern —
        // the normalizer must not alter it.
        var dp = new DividendPayment(new DateOnly(2024, 8, 15), 0.82m, CurrencyCode.USD, type);

        var result = _sut.Normalize(dp);

        result.Type.Should().Be(type);
    }
}
