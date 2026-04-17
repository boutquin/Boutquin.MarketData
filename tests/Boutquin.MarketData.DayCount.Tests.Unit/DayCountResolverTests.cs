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

public sealed class DayCountResolverTests
{
    [Fact]
    public void Resolve_ACT360_ReturnsActual360()
    {
        DayCountResolver.Resolve("ACT/360").Should().BeSameAs(Actual360.Instance);
    }

    [Fact]
    public void Resolve_ACT365F_ReturnsActual365Fixed()
    {
        DayCountResolver.Resolve("ACT/365F").Should().BeSameAs(Actual365Fixed.Instance);
    }

    [Fact]
    public void Resolve_30360_ReturnsThirty360()
    {
        DayCountResolver.Resolve("30/360").Should().BeSameAs(Thirty360.Instance);
    }

    [Fact]
    public void Resolve_AliasActual360_ReturnsActual360()
    {
        DayCountResolver.Resolve("ACTUAL/360").Should().BeSameAs(Actual360.Instance);
    }

    [Fact]
    public void Resolve_CaseInsensitive_ReturnsCorrectInstance()
    {
        DayCountResolver.Resolve("act/360").Should().BeSameAs(Actual360.Instance);
    }

    [Fact]
    public void Resolve_UnknownCode_ThrowsNotSupportedException()
    {
        var act = () => DayCountResolver.Resolve("UNKNOWN");

        act.Should().Throw<NotSupportedException>();
    }
}
