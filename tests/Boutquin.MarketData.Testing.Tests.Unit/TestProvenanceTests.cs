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

using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Testing.Builders;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Testing.Tests.Unit;

public sealed class TestProvenanceTests
{
    [Fact]
    public void ForPriceDelivery_preserves_provider_and_basis()
    {
        var basis = new PriceBasis(PriceAdjustment.All, AdjustedCloseSource.AdapterComputed, PriceAdjustment.Splits);
        var provenance = TestProvenance.ForPriceDelivery("fixture", basis);
        provenance.Provider.Value.Should().Be("fixture");
        provenance.PriceBasis.Should().Be(basis);
    }
}
