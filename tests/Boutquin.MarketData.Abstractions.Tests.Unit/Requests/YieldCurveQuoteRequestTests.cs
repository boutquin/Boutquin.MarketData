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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Requests;

public sealed class YieldCurveQuoteRequestTests
{
    private static readonly DateOnly s_asOf = new(2024, 6, 15);
    private static readonly YieldCurveId s_cadZero = new("CAD-ZERO");

    [Fact]
    public void Exchange_DefaultsToNull()
    {
        var request = new YieldCurveQuoteRequest(s_cadZero, s_asOf);

        request.Exchange.Should().BeNull();
    }

    [Fact]
    public void Exchange_IsPreserved_WhenProvided()
    {
        var request = new YieldCurveQuoteRequest(s_cadZero, s_asOf) { Exchange = ExchangeCode.XTSE };

        request.Exchange.Should().Be(ExchangeCode.XTSE);
    }

    [Fact]
    public void DatasetKey_IsStable_WhenExchangeIsNull()
    {
        var request = new YieldCurveQuoteRequest(s_cadZero, s_asOf);

        request.DatasetKey.Should().Be("CAD-ZERO");
    }

    [Fact]
    public void DatasetKey_IsScopedByExchange_WhenExchangeIsProvided()
    {
        var request = new YieldCurveQuoteRequest(s_cadZero, s_asOf) { Exchange = ExchangeCode.XTSE };

        request.DatasetKey.Should().Be("CAD-ZERO/XTSE");
    }

    // ---------- WithRange (time-series gap-fill, §2.3 step 6) ----------

    [Fact]
    public void WithRange_SetsAsOfDateFromGap_AndPreservesCurveIdAndExchange()
    {
        // A yield-curve request is a single-day snapshot: its Range is derived from AsOfDate,
        // so the only gap the pipeline can produce is that single day. WithRange sets AsOfDate
        // from the gap's start and preserves the curve identity + venue hint.
        var request = new YieldCurveQuoteRequest(s_cadZero, s_asOf) { Exchange = ExchangeCode.XTSE };
        var day = new DateOnly(2024, 7, 2);
        var gap = new DateRange(day, day);

        var narrowed = ((IDataRequest)request).WithRange(gap);

        var yc = narrowed.Should().BeOfType<YieldCurveQuoteRequest>().Subject;
        yc.AsOfDate.Should().Be(day, "the as-of date is set from the gap");
        yc.Range.Should().Be(gap, "the derived single-day range matches the gap");
        yc.CurveId.Should().Be(s_cadZero, "the curve id is preserved");
        yc.Exchange.Should().Be(ExchangeCode.XTSE, "the venue hint is preserved");
    }
}
