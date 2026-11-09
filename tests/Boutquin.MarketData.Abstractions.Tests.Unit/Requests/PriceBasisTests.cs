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
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Requests;

public sealed class PriceBasisTests
{
    [Fact]
    public void Enum_values_are_explicit() =>
        new[] { (int)PriceAdjustment.None, (int)PriceAdjustment.Splits, (int)PriceAdjustment.Dividends, (int)PriceAdjustment.All }.Should().Equal(0, 1, 2, 3);

    [Fact]
    public void Source_values_are_explicit() =>
        new[] { (int)AdjustedCloseSource.ProviderSupplied, (int)AdjustedCloseSource.AdapterComputed }.Should().Equal(1, 2);

    [Fact]
    public void Undefined_flag_bits_are_rejected()
    {
        foreach (var value in new[] { -1, 4, 7, int.MaxValue })
        {
            var adjusted = () => new PriceBasis((PriceAdjustment)value, AdjustedCloseSource.ProviderSupplied, PriceAdjustment.None);
            var ohlc = () => new PriceBasis(PriceAdjustment.All, AdjustedCloseSource.ProviderSupplied, (PriceAdjustment)value);
            adjusted.Should().Throw<ArgumentOutOfRangeException>();
            ohlc.Should().Throw<ArgumentOutOfRangeException>();
        }
        foreach (var value in new[] { 0, -1, 3 })
        {
            var source = () => new PriceBasis(PriceAdjustment.All, (AdjustedCloseSource)value, PriceAdjustment.None);
            source.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Fact]
    public void Ohlc_outside_AdjustedClose_is_rejected()
    {
        var act = () => new PriceBasis(PriceAdjustment.Splits, AdjustedCloseSource.ProviderSupplied, PriceAdjustment.Dividends);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void None_with_AdapterComputed_is_rejected()
    {
        var act = () => new PriceBasis(PriceAdjustment.None, AdjustedCloseSource.AdapterComputed, PriceAdjustment.None);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Raw_is_none_provider_none()
    {
        PriceBasis.Raw.Should().Be(new PriceBasis(PriceAdjustment.None, AdjustedCloseSource.ProviderSupplied, PriceAdjustment.None));
        PriceBasis.Raw.Token.Should().Be("none.provider.none");
    }

    [Fact]
    public void Only_raw_levels_are_window_independent()
    {
        for (var adjusted = 0; adjusted <= 3; adjusted++)
        {
            for (var ohlc = 0; ohlc <= 3; ohlc++)
            {
                if ((ohlc & ~adjusted) != 0) { continue; }
                var basis = new PriceBasis((PriceAdjustment)adjusted, AdjustedCloseSource.ProviderSupplied, (PriceAdjustment)ohlc);
                basis.LevelsAreWindowIndependent.Should().Be(adjusted == 0 && ohlc == 0);
            }
        }
    }

    [Fact]
    public void Token_literals_for_in_chain_bases()
    {
        new PriceBasis(PriceAdjustment.All, AdjustedCloseSource.ProviderSupplied, PriceAdjustment.None).Token.Should().Be("all.provider.none");
        new PriceBasis(PriceAdjustment.All, AdjustedCloseSource.AdapterComputed, PriceAdjustment.Splits).Token.Should().Be("all.computed.splits");
    }

    [Fact]
    public void Default_basis_token_throws()
    {
        var act = () => default(PriceBasis).Token;
        act.Should().Throw<InvalidOperationException>();
    }
}
