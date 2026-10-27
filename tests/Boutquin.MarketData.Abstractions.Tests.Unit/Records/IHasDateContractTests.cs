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
using Boutquin.Storage.Domain.Interfaces.TimeSeries;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

// M-1: every dated MarketData record implements Boutquin.Storage.Domain.Interfaces.TimeSeries.IHasDate.
// The interface is the minimum contract the time-series store needs to key records by
// (dataset, date). Types tested here:
//
//   Bar, ScalarObservation, FxRate, FuturesSettlement, FactorObservation, DividendPayment,
//   YieldCurveQuote.
public sealed class IHasDateContractTests
{
    private static readonly DateOnly s_sample = new(2025, 6, 15);

    [Fact]
    public void Bar_ImplementsIHasDate_ReturnsItsDate()
    {
        var record = new Bar(s_sample, Open: 100m, High: 110m, Low: 95m, Close: 105m, AdjustedClose: 105m, Volume: 1_000);

        record.Should().BeAssignableTo<IHasDate>();
        ((IHasDate)record).Date.Should().Be(s_sample);
    }

    [Fact]
    public void ScalarObservation_ImplementsIHasDate_ReturnsItsDate()
    {
        var record = new ScalarObservation(s_sample, Value: 0.0425m, Units: "decimal");

        record.Should().BeAssignableTo<IHasDate>();
        ((IHasDate)record).Date.Should().Be(s_sample);
    }

    [Fact]
    public void FxRate_ImplementsIHasDate_ReturnsItsDate()
    {
        // FxRate slimmed to (Date, Rate) per M-2.6 — the pair lives in FxRateDatasetKey.
        var record = new FxRate(s_sample, Rate: 1.0845m);

        record.Should().BeAssignableTo<IHasDate>();
        ((IHasDate)record).Date.Should().Be(s_sample);
    }

    [Fact]
    public void FuturesSettlement_ImplementsIHasDate_ReturnsItsDate()
    {
        var record = new FuturesSettlement(
            s_sample,
            ContractCode: new FuturesProductCode("SR3"),
            ContractMonth: new ContractMonth(2026, 3),
            SettlePrice: 95.25m,
            ImpliedRate: 0.0475m);

        record.Should().BeAssignableTo<IHasDate>();
        ((IHasDate)record).Date.Should().Be(s_sample);
    }

    [Fact]
    public void FactorObservation_ImplementsIHasDate_ReturnsItsDate()
    {
        var record = new FactorObservation(s_sample, FactorName: "Mkt-RF", Value: 0.0012m);

        record.Should().BeAssignableTo<IHasDate>();
        ((IHasDate)record).Date.Should().Be(s_sample);
    }

    [Fact]
    public void DividendPayment_ImplementsIHasDate_ReturnsExDateAsDate()
    {
        // DividendPayment's sort-defining date is ExDate; IHasDate.Date is the alias-via-interface.
        var record = new DividendPayment(
            ExDate: s_sample,
            Amount: 0.88m,
            Currency: Currency.USD,
            Type: DividendType.UsOrdinary);

        record.Should().BeAssignableTo<IHasDate>();
        ((IHasDate)record).Date.Should().Be(s_sample);
    }

    [Fact]
    public void YieldCurveQuote_ImplementsIHasDate_ReturnsItsDate()
    {
        var record = new YieldCurveQuote(s_sample, Tenor: "10Y", Rate: 0.045m);

        record.Should().BeAssignableTo<IHasDate>();
        ((IHasDate)record).Date.Should().Be(s_sample);
    }
}
