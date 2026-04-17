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

using Boutquin.MarketData.Abstractions.Provenance;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Provenance;

public sealed class RetrievalModeTests
{
    [Fact]
    public void MemberSet_IsExactlyFiveValues_InFixedOrdinalOrder()
    {
        Enum.GetValues<RetrievalMode>().Should().Equal(
            RetrievalMode.Api,
            RetrievalMode.Cache,
            RetrievalMode.Snapshot,
            RetrievalMode.Derived,
            RetrievalMode.Fixture);
    }

    [Theory]
    [InlineData(RetrievalMode.Api, 0)]
    [InlineData(RetrievalMode.Cache, 1)]
    [InlineData(RetrievalMode.Snapshot, 2)]
    [InlineData(RetrievalMode.Derived, 3)]
    [InlineData(RetrievalMode.Fixture, 4)]
    public void Members_HaveStableOrdinalValues(RetrievalMode value, int expectedOrdinal)
    {
        ((int)value).Should().Be(expectedOrdinal);
    }
}
