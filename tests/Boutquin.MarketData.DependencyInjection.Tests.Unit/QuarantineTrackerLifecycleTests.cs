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

using System.Reflection;

using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

public sealed class QuarantineTrackerLifecycleTests
{
    [Fact]
    public void SamePhysicalSegmentAcrossRequestWindows_IsReportedOnce()
    {
        var tracker = new QuarantineTracker();
        var first = tracker.TryRecord(0, 1, 2, new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 4)), false);
        var second = tracker.TryRecord(0, 1, 2, new DateRange(new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3)), false);

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public void IncidentDeduplication_IsBounded()
    {
        var tracker = new QuarantineTracker();
        for (var index = 0; index < 1_025; index++)
        {
            tracker.TryRecord(index, 1, 2, new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 1)), false);
        }

        var incidents = (System.Collections.IDictionary)typeof(QuarantineTracker)
            .GetField("_incidents", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(tracker)!;
        incidents.Count.Should().BeLessThanOrEqualTo(1_024);
    }
}
