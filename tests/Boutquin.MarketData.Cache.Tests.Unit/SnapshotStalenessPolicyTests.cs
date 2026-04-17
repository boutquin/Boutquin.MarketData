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
using Boutquin.MarketData.Cache.Policies;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit;

public sealed class SnapshotStalenessPolicyTests
{
    [Fact]
    public void Default_max_age_is_24_hours()
    {
        var policy = new SnapshotStalenessPolicy();

        policy.MaxAge.Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void Custom_max_age_is_respected()
    {
        var policy = new SnapshotStalenessPolicy(TimeSpan.FromMinutes(30));

        policy.MaxAge.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void IsStale_returns_false_for_fresh_snapshot()
    {
        var policy = new SnapshotStalenessPolicy(TimeSpan.FromHours(24));
        var storedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        policy.IsStale(storedAt).Should().BeFalse();
    }

    [Fact]
    public void IsStale_returns_true_for_stale_snapshot()
    {
        var policy = new SnapshotStalenessPolicy(TimeSpan.FromMinutes(30));
        var storedAt = DateTimeOffset.UtcNow.AddHours(-2);

        policy.IsStale(storedAt).Should().BeTrue();
    }

    [Fact]
    public void CheckStaleness_returns_null_for_fresh_snapshot()
    {
        var policy = new SnapshotStalenessPolicy(TimeSpan.FromHours(24));
        var storedAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        policy.CheckStaleness("test-key", storedAt).Should().BeNull();
    }

    [Fact]
    public void CheckStaleness_returns_warning_for_stale_snapshot()
    {
        var policy = new SnapshotStalenessPolicy(TimeSpan.FromMinutes(30));
        var storedAt = DateTimeOffset.UtcNow.AddHours(-2);

        var issue = policy.CheckStaleness("test-key", storedAt);

        issue.Should().NotBeNull();
        issue!.Code.Should().Be(IssueCode.SnapshotStale);
        issue.Severity.Should().Be(IssueSeverity.Warning);
        issue.Message.Should().Contain("test-key");
        issue.Message.Should().Contain("hours");
    }

    [Fact]
    public void CheckStaleness_formats_minutes_for_short_staleness()
    {
        var policy = new SnapshotStalenessPolicy(TimeSpan.FromMinutes(5));
        var storedAt = DateTimeOffset.UtcNow.AddMinutes(-15);

        var issue = policy.CheckStaleness("key", storedAt);

        issue.Should().NotBeNull();
        issue!.Message.Should().Contain("minutes");
    }
}
