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

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.DayCount.Tests.Unit;

public sealed class IcmaPublishedExamplesTests
{
    // Primary: ICMA Rule 251.3, six first-coupon examples on pp. 3–4 of the
    // ISDA-hosted excerpt (8% coupon, accrual begins February 1, 1999).
    // https://www.isda.org/a/NIJEE/ICMA-Rule-Book-Rule-251-reproduced-by-permission-of-ICMA.pdf
    // Expected fractions below transcribe the printed arithmetic, independently
    // of the production date subtraction and coupon-denominator calculation.
    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(2, 150.0 / 365.0)]
    [InlineData(3, 150.0 / 365.0 + 366.0 / 366.0)]
    [InlineData(4, 0.5)]
    [InlineData(5, 150.0 / 362.0)]
    [InlineData(6, 150.0 / 362.0 + 184.0 / 368.0)]
    public void First_coupon_examples_match_published_fractions(int example, double expected)
    {
        var calculator = ActualActualInPeriod.Instance;
        var issue = new DateOnly(1999, 2, 1);
        var july = new DateOnly(1999, 7, 1);
        var priorJuly = new DateOnly(1998, 7, 1);
        var january = new DateOnly(1999, 1, 1);
        var nextJanuary = new DateOnly(2000, 1, 1);
        var nextJuly = new DateOnly(2000, 7, 1);

        // Long coupons are explicitly composed by the caller from the two
        // notional periods prescribed by Rule 251.3, never one long denominator.
        var actual = example switch
        {
            1 => calculator.YearFraction(issue, new DateOnly(2000, 2, 1),
                issue, new DateOnly(2000, 2, 1), 1),
            2 => calculator.YearFraction(issue, july, priorJuly, july, 1),
            3 => calculator.YearFraction(issue, july, priorJuly, july, 1)
                + calculator.YearFraction(july, nextJuly, july, nextJuly, 1),
            4 => calculator.YearFraction(issue, new DateOnly(1999, 8, 1),
                issue, new DateOnly(1999, 8, 1), 2),
            5 => calculator.YearFraction(issue, july, january, july, 2),
            6 => calculator.YearFraction(issue, july, january, july, 2)
                + calculator.YearFraction(july, nextJanuary, july, nextJanuary, 2),
            _ => throw new ArgumentOutOfRangeException(nameof(example))
        };

        actual.Should().BeApproximately(expected, 1e-14);
    }
}
