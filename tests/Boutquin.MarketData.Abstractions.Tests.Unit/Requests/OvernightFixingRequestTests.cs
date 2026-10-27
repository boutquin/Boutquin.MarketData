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

using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Requests;

public sealed class OvernightFixingRequestTests
{
    private static readonly DateRange s_range = new(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
    private static readonly BenchmarkName s_sofr = new("SOFR");

    [Fact]
    public void Exchange_DefaultsToNull()
    {
        var request = new OvernightFixingRequest(s_sofr, s_range);

        request.Exchange.Should().BeNull();
    }

    [Fact]
    public void Exchange_IsPreserved_WhenProvided()
    {
        // Benchmark fixings are venue-neutral by nature, but the hint still lets adapters
        // prefer a co-located source (Bank of Canada for CORRA, Fed H.15 for SOFR, etc.).
        var request = new OvernightFixingRequest(s_sofr, s_range) { Exchange = ExchangeCode.XNYS };

        request.Exchange.Should().Be(ExchangeCode.XNYS);
    }

    [Fact]
    public void DatasetKey_IsStable_WhenExchangeIsNull()
    {
        var request = new OvernightFixingRequest(s_sofr, s_range);

        request.DatasetKey.Should().Be("SOFR");
    }

    [Fact]
    public void DatasetKey_IsScopedByExchange_WhenExchangeIsProvided()
    {
        var request = new OvernightFixingRequest(s_sofr, s_range) { Exchange = ExchangeCode.XNYS };

        request.DatasetKey.Should().Be("SOFR/XNYS");
    }
}
