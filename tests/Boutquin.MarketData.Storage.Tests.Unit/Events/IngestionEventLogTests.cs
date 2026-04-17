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
using Boutquin.MarketData.Storage.Events;
using Boutquin.Storage.Infrastructure.Events;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit.Events;

public sealed class IngestionEventLogTests
{
    private static IngestionEventLog CreateLog() =>
        new(new InMemoryEventLog<IngestionEvent>());

    private static IngestionEvent MakeEvent(string symbol, IngestionAction action) =>
        new("tiingo", "daily", symbol, action, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Append_returns_monotonically_increasing_positions()
    {
        // Arrange
        var log = CreateLog();

        // Act
        var pos0 = await log.LogAsync(MakeEvent("AAPL", IngestionAction.Fetched)).ConfigureAwait(true);
        var pos1 = await log.LogAsync(MakeEvent("MSFT", IngestionAction.Fetched)).ConfigureAwait(true);
        var pos2 = await log.LogAsync(MakeEvent("GOOG", IngestionAction.Normalized)).ConfigureAwait(true);

        // Assert
        pos0.Should().Be(0);
        pos1.Should().Be(1);
        pos2.Should().Be(2);
    }

    [Fact]
    public async Task ReadFromAsync_zero_returns_all_events_in_order()
    {
        // Arrange
        var log = CreateLog();
        await log.LogAsync(MakeEvent("AAPL", IngestionAction.Fetched)).ConfigureAwait(true);
        await log.LogAsync(MakeEvent("MSFT", IngestionAction.Cached)).ConfigureAwait(true);

        // Act
        var events = new List<(long, IngestionEvent)>();
        await foreach (var item in log.ReadFromAsync(0).ConfigureAwait(true))
        {
            events.Add(item);
        }

        // Assert
        events.Should().HaveCount(2);
        events[0].Item2.Symbol.Should().Be("AAPL");
        events[1].Item2.Symbol.Should().Be("MSFT");
    }

    [Fact]
    public async Task ReadFromAsync_N_skips_first_N_events()
    {
        // Arrange
        var log = CreateLog();
        await log.LogAsync(MakeEvent("AAPL", IngestionAction.Fetched)).ConfigureAwait(true);
        await log.LogAsync(MakeEvent("MSFT", IngestionAction.Fetched)).ConfigureAwait(true);
        await log.LogAsync(MakeEvent("GOOG", IngestionAction.Fetched)).ConfigureAwait(true);

        // Act
        var events = new List<(long, IngestionEvent)>();
        await foreach (var item in log.ReadFromAsync(2).ConfigureAwait(true))
        {
            events.Add(item);
        }

        // Assert
        events.Should().ContainSingle();
        events[0].Item2.Symbol.Should().Be("GOOG");
    }

    [Fact]
    public async Task ReadFromAsync_empty_log_yields_nothing()
    {
        // Arrange
        var log = CreateLog();

        // Act
        var events = new List<(long, IngestionEvent)>();
        await foreach (var item in log.ReadFromAsync(0).ConfigureAwait(true))
        {
            events.Add(item);
        }

        // Assert
        events.Should().BeEmpty();
    }
}
