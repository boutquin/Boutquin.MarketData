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
using Boutquin.MarketData.Storage.Checkpointing;
using Boutquin.Storage.Infrastructure.Streaming.Checkpointing;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit.Checkpointing;

public sealed class IngestionCheckpointStoreTests
{
    [Theory]
    [InlineData("", "daily", 2)]
    [InlineData("provider", " ", 2)]
    [InlineData("provider", "daily", -1)]
    public async Task Invalid_save_preserves_previous_recoverable_checkpoint(string provider, string dataset, int index)
    {
        var backing = new InMemoryCheckpointStore();
        var store = new IngestionCheckpointStore(backing);
        var valid = new IngestionCheckpoint("provider", "daily", 3, DateTimeOffset.UnixEpoch);
        await store.SaveAsync("pipeline", valid);
        var invalid = new IngestionCheckpoint(provider, dataset, index, DateTimeOffset.UnixEpoch);

        var save = () => store.SaveAsync("pipeline", invalid);
        await save.Should().ThrowAsync<ArgumentException>("invalid input must be rejected before durable replacement");
        (await new IngestionCheckpointStore(backing).LoadAsync("pipeline")).Should().Be(valid);
    }

    private static IngestionCheckpointStore CreateStore() =>
        new(new InMemoryCheckpointStore());

    [Fact]
    public async Task Save_then_load_returns_checkpoint()
    {
        // Arrange
        var store = CreateStore();
        var checkpoint = new IngestionCheckpoint("tiingo", "daily", 5, DateTimeOffset.UtcNow);

        // Act
        await store.SaveAsync("tiingo-daily", checkpoint).ConfigureAwait(true);
        var loaded = await store.LoadAsync("tiingo-daily").ConfigureAwait(true);

        // Assert
        loaded.Should().BeEquivalentTo(checkpoint);
    }

    [Fact]
    public async Task Load_missing_pipelineId_returns_null()
    {
        // Arrange
        var store = CreateStore();

        // Act
        var loaded = await store.LoadAsync("nonexistent").ConfigureAwait(true);

        // Assert
        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Save_twice_returns_latest_on_load()
    {
        // Arrange
        var store = CreateStore();
        var first = new IngestionCheckpoint("fred", "rates", 0, DateTimeOffset.UtcNow.AddHours(-1));
        var second = new IngestionCheckpoint("fred", "rates", 3, DateTimeOffset.UtcNow);

        // Act
        await store.SaveAsync("fred-rates", first).ConfigureAwait(true);
        await store.SaveAsync("fred-rates", second).ConfigureAwait(true);
        var loaded = await store.LoadAsync("fred-rates").ConfigureAwait(true);

        // Assert
        loaded.Should().BeEquivalentTo(second);
    }

    [Fact]
    public async Task Multiple_pipelineIds_are_independent()
    {
        // Arrange
        var store = CreateStore();
        var cp1 = new IngestionCheckpoint("tiingo", "daily", 2, DateTimeOffset.UtcNow);
        var cp2 = new IngestionCheckpoint("fred", "rates", 7, DateTimeOffset.UtcNow);

        // Act
        await store.SaveAsync("pipeline-1", cp1).ConfigureAwait(true);
        await store.SaveAsync("pipeline-2", cp2).ConfigureAwait(true);

        // Assert
        var loaded1 = await store.LoadAsync("pipeline-1").ConfigureAwait(true);
        var loaded2 = await store.LoadAsync("pipeline-2").ConfigureAwait(true);
        loaded1.Should().BeEquivalentTo(cp1);
        loaded2.Should().BeEquivalentTo(cp2);
    }
}
