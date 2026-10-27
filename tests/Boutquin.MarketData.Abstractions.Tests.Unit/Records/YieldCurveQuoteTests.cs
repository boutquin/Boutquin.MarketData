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

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

public sealed class YieldCurveQuoteTests
{
    [Fact]
    public void Construction_PreservesAllFields()
    {
        // Arrange
        var date = new DateOnly(2026, 4, 10);

        // Act
        var quote = new YieldCurveQuote(date, "10Y", 0.045m);

        // Assert
        quote.Date.Should().Be(date);
        quote.Tenor.Should().Be("10Y");
        quote.Rate.Should().Be(0.045m);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        // Arrange
        var date = new DateOnly(2026, 4, 10);
        var quote1 = new YieldCurveQuote(date, "5Y", 0.038m);
        var quote2 = new YieldCurveQuote(date, "5Y", 0.038m);

        // Act & Assert
        quote1.Should().Be(quote2);
    }
}
