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

public sealed class PriceBasisRequestKeyTests
{
    private static readonly DateRange s_range = new(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 16));

    [Fact]
    public void DatasetKey_spy_tiingo_basis() =>
        new PriceHistoryRequest([new Symbol("SPY")], s_range) { Basis = new(PriceAdjustment.All, AdjustedCloseSource.ProviderSupplied, PriceAdjustment.None) }
            .DatasetKey.Should().Be("SPY/basis:all.provider.none");

    [Fact]
    public void DatasetKey_xic_xtse_twelvedata_basis() =>
        new PriceHistoryRequest([new Symbol("XIC")], s_range) { Exchange = ExchangeCode.XTSE, Basis = new(PriceAdjustment.All, AdjustedCloseSource.AdapterComputed, PriceAdjustment.Splits) }
            .DatasetKey.Should().Be("XIC/XTSE/basis:all.computed.splits");

    [Fact]
    public void Requests_differing_only_in_basis_are_unequal()
    {
        var request = new PriceHistoryRequest([new Symbol("SPY")], s_range) { Basis = PriceBasis.Raw };
        (request with { Basis = new(PriceAdjustment.All, AdjustedCloseSource.ProviderSupplied, PriceAdjustment.None) }).Should().NotBe(request);
    }

    [Fact]
    public void Default_basis_init_throws()
    {
        var act = () => new PriceHistoryRequest([new Symbol("SPY")], s_range) { Basis = default };
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
