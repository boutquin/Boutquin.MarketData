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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Cache.TimeSeries;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit.TimeSeries;

public sealed class InMemoryAttemptedRangeIndexTests
{
    [Fact]
    public void Capacity_evicts_the_least_recently_used_identity()
    {
        var clock = new FakeClock();
        var index = new InMemoryAttemptedRangeIndex(2, TimeSpan.FromHours(1), clock);
        var date = new DateOnly(2026, 1, 1);

        index.RecordAttempt("A", date, date);
        clock.Advance(TimeSpan.FromMinutes(1));
        index.RecordAttempt("B", date, date);
        clock.Advance(TimeSpan.FromMinutes(1));
        index.RecordAttempt("C", date, date);

        index.GetAttemptedRanges("A").Should().BeEmpty();
        index.GetAttemptedRanges("B").Should().ContainSingle();
        index.GetAttemptedRanges("C").Should().ContainSingle();
    }

    [Fact]
    public void Expiry_removes_an_attempted_identity_using_the_injected_clock()
    {
        var clock = new FakeClock();
        var index = new InMemoryAttemptedRangeIndex(2, TimeSpan.FromMinutes(5), clock);
        var date = new DateOnly(2026, 1, 1);
        index.RecordAttempt("A", date, date);

        clock.Advance(TimeSpan.FromMinutes(5));

        index.GetAttemptedRanges("A").Should().BeEmpty();
    }

    [Fact]
    public void ReturnedRanges_AreAnImmutableSnapshot()
    {
        var index = new InMemoryAttemptedRangeIndex(2, TimeSpan.FromHours(1), new FakeClock());
        index.RecordAttempt("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));
        index.RecordAttempt("A", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 2));

        var returned = index.GetAttemptedRanges("A");
        var mutate = () => ((IList<(DateOnly From, DateOnly To)>)returned).Add(
            (new DateOnly(2030, 1, 1), new DateOnly(2030, 1, 2)));

        mutate.Should().Throw<NotSupportedException>();
        index.GetAttemptedRanges("A").Should().NotContain(x => x.From.Year == 2030);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan duration) => UtcNow += duration;
    }
}
