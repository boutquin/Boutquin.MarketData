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

using Boutquin.MarketData.Normalization.Normalizers;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Normalization.Tests.Unit;

public sealed class RcPrecisionContractTests
{
    [Theory]
    [InlineData(0, -1)]
    [InlineData(0, 29)]
    [InlineData(1, -1)]
    [InlineData(1, 29)]
    [InlineData(2, -1)]
    [InlineData(2, 29)]
    [InlineData(3, -1)]
    [InlineData(3, 29)]
    public void Constructors_reject_invalid_precision_immediately(int kind, int precision)
    {
        var construct = () => Construct(kind, precision);
        construct.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("decimalPlaces");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 28)]
    [InlineData(1, 0)]
    [InlineData(1, 28)]
    [InlineData(2, 0)]
    [InlineData(2, 28)]
    [InlineData(3, 0)]
    [InlineData(3, 28)]
    public void Constructors_accept_decimal_rounding_boundaries(int kind, int precision)
    {
        var construct = () => Construct(kind, precision);
        construct.Should().NotThrow();
    }

    private static object Construct(int kind, int precision) => kind switch
    {
        0 => new BarNormalizer(precision),
        1 => new FxRateNormalizer(precision),
        2 => new FuturesSettlementNormalizer(precision),
        3 => new OptionQuoteNormalizer(precision),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
