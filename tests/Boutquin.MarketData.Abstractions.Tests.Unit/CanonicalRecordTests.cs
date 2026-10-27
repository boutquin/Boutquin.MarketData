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

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit;

public sealed class CorporateActionTests
{
    [Fact]
    public void CashDividend_uses_Amount_not_Factor()
    {
        var action = new CorporateAction(
            new DateOnly(2024, 8, 15), new Symbol("AAPL"),
            CorporateActionType.CashDividend,
            Factor: null, Amount: 0.82m, Currency: Currency.USD,
            Description: "Quarterly dividend");

        action.ActionType.Should().Be(CorporateActionType.CashDividend);
        action.Amount.Should().Be(0.82m);
        action.Factor.Should().BeNull();
    }

    [Fact]
    public void Split_uses_Factor_not_Amount()
    {
        var action = new CorporateAction(
            new DateOnly(2024, 8, 9), new Symbol("NVDA"),
            CorporateActionType.Split,
            Factor: 10.0m, Amount: null, Currency: null,
            Description: "10-for-1 stock split");

        action.ActionType.Should().Be(CorporateActionType.Split);
        action.Factor.Should().Be(10.0m);
        action.Amount.Should().BeNull();
    }

    [Fact]
    public void SpinOff_carries_distribution_ratio()
    {
        var action = new CorporateAction(
            new DateOnly(2024, 4, 3), new Symbol("GE"),
            CorporateActionType.SpinOff,
            Factor: 0.25m, Amount: null, Currency: null,
            Description: "GE Vernova spin-off");

        action.Factor.Should().Be(0.25m);
    }

    [Fact]
    public void Record_equality_works()
    {
        var a = new CorporateAction(new DateOnly(2024, 1, 1), new Symbol("AAPL"), CorporateActionType.Split, 4.0m, null, null, null);
        var b = new CorporateAction(new DateOnly(2024, 1, 1), new Symbol("AAPL"), CorporateActionType.Split, 4.0m, null, null, null);

        a.Should().Be(b);
    }

    [Fact]
    public void All_CorporateActionType_values_are_defined()
    {
        Enum.GetValues<CorporateActionType>().Should().HaveCount(4);
    }
}

public sealed class OptionQuoteTests
{
    // M-2.7: OptionQuote slimmed to (Date, Bid?, Ask?, LastPrice?, ImpliedVolatility?, OpenInterest,
    // Volume). The contract identity (Underlying, Expiry, Strike, Type) moved into
    // OptionQuoteDatasetKey (operator decision A / §2.5.1), so the body no longer carries those fields;
    // the prior Underlying/Expiry/Strike/Type body assertions are removed for that reason. Every
    // surviving body field is still asserted below.
    [Fact]
    public void Construction_preserves_all_fields()
    {
        var quote = new OptionQuote(
            new DateOnly(2024, 6, 15),
            Bid: 5.20m, Ask: 5.40m, LastPrice: 5.30m,
            ImpliedVolatility: 0.25m,
            OpenInterest: 15_000, Volume: 2_500);

        quote.Date.Should().Be(new DateOnly(2024, 6, 15));
        quote.Bid.Should().Be(5.20m);
        quote.Ask.Should().Be(5.40m);
        quote.LastPrice.Should().Be(5.30m);
        quote.ImpliedVolatility.Should().Be(0.25m);
        quote.OpenInterest.Should().Be(15_000);
        quote.Volume.Should().Be(2_500);
    }

    [Fact]
    public void Nullable_fields_can_be_null()
    {
        var quote = new OptionQuote(
            new DateOnly(2024, 6, 15),
            Bid: null, Ask: null, LastPrice: null,
            ImpliedVolatility: null,
            OpenInterest: 0, Volume: 0);

        quote.Bid.Should().BeNull();
        quote.Ask.Should().BeNull();
        quote.LastPrice.Should().BeNull();
        quote.ImpliedVolatility.Should().BeNull();
    }

    [Fact]
    public void Record_equality_works()
    {
        var a = new OptionQuote(new DateOnly(2024, 1, 1), 1m, 2m, 1.5m, 0.2m, 100, 50);
        var b = new OptionQuote(new DateOnly(2024, 1, 1), 1m, 2m, 1.5m, 0.2m, 100, 50);

        a.Should().Be(b);
    }

    [Fact]
    public void OptionType_has_Call_and_Put()
    {
        Enum.GetValues<OptionType>().Should().BeEquivalentTo([OptionType.Call, OptionType.Put]);
    }
}
