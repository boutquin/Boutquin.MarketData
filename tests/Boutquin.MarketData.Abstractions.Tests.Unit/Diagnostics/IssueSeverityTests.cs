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

using Boutquin.MarketData.Abstractions.Diagnostics;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Diagnostics;

public sealed class IssueSeverityTests
{
    [Fact]
    public void MemberSet_IsExactlyWarningAndError()
    {
        Enum.GetValues<IssueSeverity>().Should().Equal(IssueSeverity.Warning, IssueSeverity.Error);
    }

    [Theory]
    [InlineData(IssueSeverity.Warning, 0)]
    [InlineData(IssueSeverity.Error, 1)]
    public void Members_HaveStableOrdinalValues(IssueSeverity value, int expectedOrdinal)
    {
        ((int)value).Should().Be(expectedOrdinal);
    }
}
