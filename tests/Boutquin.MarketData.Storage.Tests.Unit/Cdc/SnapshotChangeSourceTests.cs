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
using System.Text;

using Boutquin.MarketData.Storage.Cdc;
using Boutquin.Storage.Domain.Enums;
using Boutquin.Storage.Domain.ValueObjects;
using Boutquin.Storage.Infrastructure.Cdc;
using Boutquin.Storage.Infrastructure.Events;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit.Cdc;

public sealed class SnapshotChangeSourceTests
{
    private static (SnapshotChangeSource Source, InMemoryEventLog<ChangeRecord<string, byte[]>> EventLog) CreateSource()
    {
        var eventLog = new InMemoryEventLog<ChangeRecord<string, byte[]>>();
        var cdcSource = new EventLogCdcSource<string, byte[]>(eventLog);
        var source = new SnapshotChangeSource(cdcSource);
        return (source, eventLog);
    }

    [Fact]
    public async Task Delegates_to_inner_source()
    {
        // Arrange
        var (source, eventLog) = CreateSource();
        var record = new ChangeRecord<string, byte[]>(
            0, ChangeOperation.Insert, "key-1", Encoding.UTF8.GetBytes("data"), DateTimeOffset.UtcNow);
        await eventLog.AppendAsync(record).ConfigureAwait(true);

        // Act
        var changes = new List<ChangeRecord<string, byte[]>>();
        await foreach (var change in source.ReadChangesAsync(0).ConfigureAwait(true))
        {
            changes.Add(change);
        }

        // Assert
        changes.Should().ContainSingle();
        changes[0].Key.Should().Be("key-1");
        changes[0].Operation.Should().Be(ChangeOperation.Insert);
    }

    [Fact]
    public async Task Position_filtering_works()
    {
        // Arrange
        var (source, eventLog) = CreateSource();
        var record0 = new ChangeRecord<string, byte[]>(
            0, ChangeOperation.Insert, "first", Encoding.UTF8.GetBytes("a"), DateTimeOffset.UtcNow);
        var record1 = new ChangeRecord<string, byte[]>(
            1, ChangeOperation.Update, "second", Encoding.UTF8.GetBytes("b"), DateTimeOffset.UtcNow);
        await eventLog.AppendAsync(record0).ConfigureAwait(true);
        await eventLog.AppendAsync(record1).ConfigureAwait(true);

        // Act — read from position 1
        var changes = new List<ChangeRecord<string, byte[]>>();
        await foreach (var change in source.ReadChangesAsync(1).ConfigureAwait(true))
        {
            changes.Add(change);
        }

        // Assert
        changes.Should().ContainSingle();
        changes[0].Key.Should().Be("second");
    }
}
