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
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Orchestration.Pipeline;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Orchestration.Tests.Unit.Pipeline;

public sealed class TimeSeriesSelectedProviderIndexTests
{
    [Fact]
    public void Capacity_evicts_the_least_recently_used_provider_attribution()
    {
        var clock = new SelectedProviderFakeClock();
        var index = new InMemoryTimeSeriesSelectedProviderIndex(2, TimeSpan.FromHours(1), clock);

        index.Record(typeof(TestRequest), "A", new ProviderCode("provider-a"));
        clock.Advance(TimeSpan.FromMinutes(1));
        index.Record(typeof(TestRequest), "B", new ProviderCode("provider-b"));
        clock.Advance(TimeSpan.FromMinutes(1));
        index.Record(typeof(TestRequest), "C", new ProviderCode("provider-c"));

        index.TryGet(typeof(TestRequest), "A", out _).Should().BeFalse();
        index.TryGet(typeof(TestRequest), "B", out var providerB).Should().BeTrue();
        providerB.Should().Be(new ProviderCode("provider-b"));
    }

    [Fact]
    public void Expiry_removes_provider_attribution_using_the_injected_clock()
    {
        var clock = new SelectedProviderFakeClock();
        var index = new InMemoryTimeSeriesSelectedProviderIndex(2, TimeSpan.FromMinutes(5), clock);
        index.Record(typeof(TestRequest), "A", new ProviderCode("provider-a"));

        clock.Advance(TimeSpan.FromMinutes(5));

        index.TryGet(typeof(TestRequest), "A", out _).Should().BeFalse();
    }

    private sealed class SelectedProviderFakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan duration) => UtcNow += duration;
    }
}
