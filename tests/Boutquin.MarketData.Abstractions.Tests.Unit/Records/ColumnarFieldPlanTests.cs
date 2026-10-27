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
using Boutquin.Storage.Domain.Enums;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

// M-2 generator smoke tests for the columnar field plan.
//
// Each [ColumnarStorable]-annotated record gets a `ColumnarFieldPlan` static property
// emitted by Boutquin.Storage.SourceGenerator. The plan lists every property with the
// codec the generator selected from its CLR type (no [ColumnEncoding] overrides are
// applied in MarketData — generator defaults are correct).
//
// These tests lock in the per-record codec plans. If the generator default for a given
// CLR type changes upstream, these tests will fail — a regression we want visible.
public sealed class ColumnarFieldPlanTests
{
    [Fact]
    public void Bar_ColumnarFieldPlan_MatchesSpec()
    {
        var plan = Bar.ColumnarFieldPlan;

        plan.Should().HaveCount(7);
        plan[0].Name.Should().Be("Date");
        plan[0].Encoding.Should().Be(ColumnEncoding.DeltaOfDeltaVlq);

        // Open / High / Low / Close / AdjustedClose — all decimal, Dictionary default.
        for (var i = 1; i <= 5; i++)
        {
            plan[i].Encoding.Should().Be(ColumnEncoding.Dictionary, because: $"decimal field {plan[i].Name} gets Dictionary default");
        }

        plan[6].Name.Should().Be("Volume");
        plan[6].Encoding.Should().Be(ColumnEncoding.DeltaVlq);
    }

    [Fact]
    public void ScalarObservation_ColumnarFieldPlan_MatchesSpec()
    {
        var plan = ScalarObservation.ColumnarFieldPlan;

        plan.Should().HaveCount(3);
        plan[0].Name.Should().Be("Date");
        plan[0].Encoding.Should().Be(ColumnEncoding.DeltaOfDeltaVlq);
        plan[1].Name.Should().Be("Value");
        plan[1].Encoding.Should().Be(ColumnEncoding.Dictionary);
        plan[2].Name.Should().Be("Units");
        plan[2].Encoding.Should().Be(ColumnEncoding.Dictionary);
    }

    [Fact]
    public void FxRate_ColumnarFieldPlan_IncludesDateAndRate()
    {
        var plan = FxRate.ColumnarFieldPlan;

        plan.Should().Contain(d => d.Name == "Date" && d.Encoding == ColumnEncoding.DeltaOfDeltaVlq);
        plan.Should().Contain(d => d.Name == "Rate" && d.Encoding == ColumnEncoding.Dictionary);
    }

    [Fact]
    public void FactorObservation_ColumnarFieldPlan_MatchesSpec()
    {
        var plan = FactorObservation.ColumnarFieldPlan;

        plan.Should().HaveCount(3);
        plan[0].Name.Should().Be("Date");
        plan[0].Encoding.Should().Be(ColumnEncoding.DeltaOfDeltaVlq);
        plan[1].Name.Should().Be("FactorName");
        plan[1].Encoding.Should().Be(ColumnEncoding.Dictionary);
        plan[2].Name.Should().Be("Value");
        plan[2].Encoding.Should().Be(ColumnEncoding.Dictionary);
    }

    [Fact]
    public void YieldCurveQuote_ColumnarFieldPlan_MatchesSpec()
    {
        var plan = YieldCurveQuote.ColumnarFieldPlan;

        plan.Should().HaveCount(3);
        plan[0].Name.Should().Be("Date");
        plan[0].Encoding.Should().Be(ColumnEncoding.DeltaOfDeltaVlq);
        plan[1].Name.Should().Be("Tenor");
        plan[1].Encoding.Should().Be(ColumnEncoding.Dictionary);
        plan[2].Name.Should().Be("Rate");
        plan[2].Encoding.Should().Be(ColumnEncoding.Dictionary);
    }

    // NOTE (M-CR): FuturesSettlement and DividendPayment are the row opt-out per §2.6.1 — they no
    // longer carry [ColumnarStorable], so they expose no generated `ColumnarFieldPlan`. The two
    // tests that asserted their columnar field plans were removed because the milestone deliberately
    // moves these records off the columnar path (FuturesSettlement: wrapper + nullable types trip
    // BSSG020/BSSG001; DividendPayment: row opt-out, §2.6.1 default). Their serialization is covered
    // by RecordRoundTripTests on the tagged-wire / IHasDate surface instead.
}
