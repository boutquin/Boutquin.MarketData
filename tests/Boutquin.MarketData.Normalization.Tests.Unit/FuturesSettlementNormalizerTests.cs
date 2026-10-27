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

public sealed class FuturesSettlementNormalizerTests
{
    private readonly FuturesSettlementNormalizer _sut = new();

    private static readonly ContractMonth s_jun2025 = new(2025, 6);

    [Fact]
    public void Rejects_null_required_contract_code_with_member_diagnostic()
    {
        var act = () => _sut.Normalize(default);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("raw")
            .WithMessage("*ContractCode*");
    }

    [Fact]
    public void Rejects_default_contract_code_in_otherwise_initialized_record_with_member_diagnostic()
    {
        var settle = new FuturesSettlement(new DateOnly(2024, 6, 15), default, s_jun2025, 95.50m, null);

        var act = () => _sut.Normalize(settle);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("raw")
            .WithMessage("*ContractCode*");
    }

    [Fact]
    public void Uppercases_contract_code()
    {
        var settle = new FuturesSettlement(new DateOnly(2024, 6, 15), new FuturesProductCode("sr3"), s_jun2025, 95.50m, null);

        var result = _sut.Normalize(settle);

        result.ContractCode.Should().Be(new FuturesProductCode("SR3"));
    }

    [Fact]
    public void ContractMonth_constructed_from_YYYYMM_renders_canonical_YYYY_MM()
    {
        // YYYYMM → YYYY-MM normalisation moved into the ContractMonth constructor itself.
        var month = new ContractMonth("202506");

        month.ToString().Should().Be("2025-06");
    }

    [Fact]
    public void Preserves_valid_contract_month()
    {
        var settle = new FuturesSettlement(new DateOnly(2024, 6, 15), new FuturesProductCode("SR3"), s_jun2025, 95.50m, null);

        var result = _sut.Normalize(settle);

        result.ContractMonth.Should().Be(s_jun2025);
    }

    [Theory]
    [InlineData("SR3")]
    [InlineData("ZQ")]
    public void Computes_decimal_implied_rate_for_supported_interest_rate_product_when_null(string productCode)
    {
        var settle = new FuturesSettlement(
            new DateOnly(2024, 6, 15),
            new FuturesProductCode(productCode),
            s_jun2025,
            95.50m,
            null);

        var result = _sut.Normalize(settle);

        result.ImpliedRate.Should().Be(0.0450m);
    }

    [Fact]
    public void Leaves_implied_rate_null_for_non_rate_product()
    {
        var settle = new FuturesSettlement(
            new DateOnly(2024, 6, 15),
            new FuturesProductCode("ES"),
            s_jun2025,
            5350.25m,
            null);

        var result = _sut.Normalize(settle);

        result.ImpliedRate.Should().BeNull();
    }

    [Fact]
    public void Preserves_existing_implied_rate()
    {
        var settle = new FuturesSettlement(new DateOnly(2024, 6, 15), new FuturesProductCode("SR3"), s_jun2025, 95.50m, 0.045m);

        var result = _sut.Normalize(settle);

        result.ImpliedRate.Should().Be(0.045m);
    }

    [Fact]
    public void Rounds_settle_price()
    {
        var settle = new FuturesSettlement(new DateOnly(2024, 6, 15), new FuturesProductCode("SR3"), s_jun2025, 95.505555m, null);

        var result = _sut.Normalize(settle);

        result.SettlePrice.Should().Be(95.5056m);
    }

    [Fact]
    public void Preserves_date()
    {
        var date = new DateOnly(2024, 3, 15);
        var settle = new FuturesSettlement(date, new FuturesProductCode("ZQ"), new ContractMonth(2024, 6), 94.75m, null);

        var result = _sut.Normalize(settle);

        result.Date.Should().Be(date);
    }
}
