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

public sealed class FactorSeriesRequestTests
{
    private static readonly DateRange s_range = new(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
    private static readonly FactorDatasetId s_datasetId = new("F-F_Research_Data_Factors_daily");

    [Fact]
    public void Exchange_DefaultsToNull()
    {
        var request = new FactorSeriesRequest(s_datasetId, s_range);

        request.Exchange.Should().BeNull();
    }

    [Fact]
    public void Exchange_IsPreserved_WhenProvided()
    {
        // Fama-French factors are US-equity-market factors; explicit XNYS hint lets a
        // multi-region portfolio reason about which factor set applies to which leg.
        var request = new FactorSeriesRequest(s_datasetId, s_range) { Exchange = ExchangeCode.XNYS };

        request.Exchange.Should().Be(ExchangeCode.XNYS);
    }

    [Fact]
    public void DatasetKey_IsStable_WhenExchangeIsNull()
    {
        var request = new FactorSeriesRequest(s_datasetId, s_range);

        request.DatasetKey.Should().Be("F-F_Research_Data_Factors_daily");
    }

    [Fact]
    public void DatasetKey_IsScopedByExchange_WhenExchangeIsProvided()
    {
        var request = new FactorSeriesRequest(s_datasetId, s_range) { Exchange = ExchangeCode.XNYS };

        request.DatasetKey.Should().Be("F-F_Research_Data_Factors_daily/XNYS");
    }

    // ---------- WithRange (time-series gap-fill, §2.3 step 6) ----------

    [Fact]
    public void WithRange_NarrowsRange_AndPreservesDatasetNameFrequencyAndExchange()
    {
        var request = new FactorSeriesRequest(s_datasetId, s_range, DataFrequency.Monthly)
        { Exchange = ExchangeCode.XNYS };
        var gap = new DateRange(new DateOnly(2024, 6, 1), new DateOnly(2024, 6, 30));

        var narrowed = ((IDataRequest)request).WithRange(gap);

        var factor = narrowed.Should().BeOfType<FactorSeriesRequest>().Subject;
        factor.Range.Should().Be(gap, "only the range is narrowed to the gap");
        factor.DatasetName.Should().Be(s_datasetId, "the factor dataset name is preserved");
        factor.Frequency.Should().Be(DataFrequency.Monthly, "the frequency is preserved");
        factor.Exchange.Should().Be(ExchangeCode.XNYS, "the venue hint is preserved");
    }
}
