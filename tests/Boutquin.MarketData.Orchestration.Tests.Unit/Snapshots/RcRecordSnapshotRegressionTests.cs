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
using Boutquin.MarketData.Orchestration.Commands;
using Boutquin.MarketData.Orchestration.Snapshots;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Orchestration.Tests.Unit.Snapshots;

public sealed class RcRecordSnapshotRegressionTests
{
    [Fact]
    public void Snapshot_result_copies_constructor_and_init_collections_and_protects_getter()
    {
        var source = new List<int> { 7 };
        var original = new SnapshotReadResult<int>(source, false);
        source[0] = 99;
        original.Records.Should().Equal(7);
        var replacement = new List<int> { 8 };
        var updated = original with { Records = replacement };
        replacement.Clear();
        updated.Records.Should().Equal(8);
        original.Records.Should().Equal(7);
        Action mutate = () => ((IList<int>)updated.Records)[0] = 99;
        mutate.Should().Throw<NotSupportedException>();
        var (records, hasGap) = updated;
        records.Should().Equal(8);
        hasGap.Should().BeFalse();
    }

    [Fact]
    public void Legacy_snapshot_copies_constructor_and_init_collections_and_protects_getter()
    {
        var request = new PriceHistoryRequest([new Symbol("AGG")], new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));
        var source = new List<int> { 7 };
        var original = new LegacyL2Snapshot<PriceHistoryRequest, int>(request, source);
        source[0] = 99;
        original.Records.Should().Equal(7);
        var replacement = new List<int> { 8 };
        var updated = original with { Records = replacement };
        replacement.Clear();
        updated.Records.Should().Equal(8);
        original.Records.Should().Equal(7);
        Action mutate = () => ((IList<int>)updated.Records)[0] = 99;
        mutate.Should().Throw<NotSupportedException>();
        var (copiedRequest, records) = updated;
        copiedRequest.Should().BeSameAs(request);
        records.Should().Equal(8);
    }
}
