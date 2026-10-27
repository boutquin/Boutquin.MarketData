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

public sealed class CorporateActionNormalizerTests
{
    private readonly CorporateActionNormalizer _sut = new();

    [Fact]
    public void Rejects_null_record_with_shared_raw_parameter_contract()
    {
        var act = () => _sut.Normalize(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("raw");
    }

    [Fact]
    public void Preserves_null_optional_description()
    {
        var action = new CorporateAction(
            new DateOnly(2024, 8, 9),
            new Symbol("NVDA"),
            CorporateActionType.Split,
            4.0m,
            null,
            null,
            null);

        var result = _sut.Normalize(action);

        result.Description.Should().BeNull();
    }

    [Fact]
    public void Preserves_symbol()
    {
        var action = new CorporateAction(
            new DateOnly(2024, 8, 9), new Symbol("NVDA"), CorporateActionType.Split,
            4.0m, null, null, null);

        var result = _sut.Normalize(action);

        result.Symbol.Should().Be(new Symbol("NVDA"));
    }

    [Fact]
    public void Preserves_currency()
    {
        var action = new CorporateAction(
            new DateOnly(2024, 8, 15), new Symbol("AAPL"), CorporateActionType.CashDividend,
            null, 0.82m, Currency.USD, null);

        var result = _sut.Normalize(action);

        result.Currency.Should().Be(Currency.USD);
    }

    [Fact]
    public void Null_currency_stays_null()
    {
        var action = new CorporateAction(
            new DateOnly(2024, 8, 9), new Symbol("NVDA"), CorporateActionType.Split,
            4.0m, null, null, null);

        var result = _sut.Normalize(action);

        result.Currency.Should().BeNull();
    }

    [Fact]
    public void Clamps_negative_dividend_amount_to_zero()
    {
        var action = new CorporateAction(
            new DateOnly(2024, 8, 15), new Symbol("AAPL"), CorporateActionType.CashDividend,
            null, -0.50m, Currency.USD, null);

        var result = _sut.Normalize(action);

        result.Amount.Should().Be(0m);
    }

    [Fact]
    public void Clamps_negative_split_factor_to_positive()
    {
        var action = new CorporateAction(
            new DateOnly(2024, 8, 9), new Symbol("NVDA"), CorporateActionType.Split,
            -4.0m, null, null, null);

        var result = _sut.Normalize(action);

        result.Factor.Should().Be(4.0m);
    }

    [Fact]
    public void Trims_description()
    {
        var action = new CorporateAction(
            new DateOnly(2024, 8, 9), new Symbol("NVDA"), CorporateActionType.Split,
            10.0m, null, null, "  10-for-1 stock split  ");

        var result = _sut.Normalize(action);

        result.Description.Should().Be("10-for-1 stock split");
    }

    [Fact]
    public void Null_description_stays_null()
    {
        var action = new CorporateAction(
            new DateOnly(2024, 8, 9), new Symbol("NVDA"), CorporateActionType.Split,
            10.0m, null, null, null);

        var result = _sut.Normalize(action);

        result.Description.Should().BeNull();
    }

    [Fact]
    public void Preserves_date_and_action_type()
    {
        var date = new DateOnly(2024, 4, 3);
        var action = new CorporateAction(
            date, new Symbol("GE"), CorporateActionType.SpinOff,
            0.25m, null, null, "GE Vernova");

        var result = _sut.Normalize(action);

        result.EffectiveDate.Should().Be(date);
        result.ActionType.Should().Be(CorporateActionType.SpinOff);
    }
}
