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

/// <summary>
/// Asserts the alias additions introduced by the reference-data consolidation spec
/// (§2.7). Legacy alias coverage lives in <c>DayCountResolverTests</c>.
/// </summary>
public sealed class DayCountResolverAliasTests
{
    [Theory]
    [InlineData("ACT/ACT")]
    [InlineData("act/act")]
    [InlineData("ACTUAL/ACTUAL")]
    [InlineData("ACT/ACT ICMA")]
    [InlineData("act/act icma")]
    [InlineData("ACT/ACT BOND")]
    public void Actual_actual_aliases_resolve_to_in_period(string code)
    {
        DayCountResolver.Resolve(code).Should().BeSameAs(ActualActualInPeriod.Instance);
    }

    [Theory]
    [InlineData("30E/360")]
    [InlineData("30e/360")]
    [InlineData("EUROBOND")]
    [InlineData("eurobond")]
    [InlineData("EUROBOND BASIS")]
    public void Thirty_e_360_aliases_resolve_correctly(string code)
    {
        DayCountResolver.Resolve(code).Should().BeSameAs(ThirtyE360.Instance);
    }

    [Fact]
    public void Unknown_code_still_throws_NotSupportedException()
    {
        var act = () => DayCountResolver.Resolve("BUS/252");

        act.Should().Throw<NotSupportedException>()
           .WithMessage("*BUS/252*");
    }
}
