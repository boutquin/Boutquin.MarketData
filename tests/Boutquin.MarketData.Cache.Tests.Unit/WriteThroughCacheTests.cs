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
using System.Text.Json;

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Cache.WriteThrough;
using Boutquin.MarketData.Storage.InMemory;
using Boutquin.MarketData.Storage.Serializers;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit;

public sealed class WriteThroughCacheTests
{
    [Fact]
    public async Task Null_overwrite_rejects_and_preserves_previous_snapshot_for_a_fresh_reader()
    {
        var store = new InMemorySnapshotStore();
        var cache = new WriteThroughCache<string, string?>(store, new JsonSnapshotSerializer<string?>(), key => key);
        await cache.WriteAsync("preserved", "original");

        var act = () => cache.WriteAsync("preserved", null);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("data");
        var freshReader = new WriteThroughCache<string, string?>(store, new JsonSnapshotSerializer<string?>(), key => key);
        (await freshReader.TryReadAsync("preserved")).Should().Be("original");
    }

    [Fact]
    public async Task GetOrFetchAsync_on_miss_calls_factory_and_caches()
    {
        var (cache, store) = CreateCache();
        var callCount = 0;

        var result = await cache.GetOrFetchAsync("key-1", (k, _) =>
        {
            callCount++;
            return Task.FromResult(new TestPayload(k, 42));
        });

        result.Name.Should().Be("key-1");
        result.Value.Should().Be(42);
        callCount.Should().Be(1);
        (await store.ExistsAsync("key-1")).Should().BeTrue();
    }

    [Fact]
    public async Task GetOrFetchAsync_on_hit_returns_cached_without_factory()
    {
        var (cache, _) = CreateCache();
        await cache.WriteAsync("key-1", new TestPayload("cached", 99));
        var callCount = 0;

        var result = await cache.GetOrFetchAsync("key-1", (_, _) =>
        {
            callCount++;
            return Task.FromResult(new TestPayload("factory", 0));
        });

        result.Name.Should().Be("cached");
        result.Value.Should().Be(99);
        callCount.Should().Be(0);
    }

    [Fact]
    public async Task TryReadAsync_returns_null_when_key_not_found()
    {
        var (cache, _) = CreateCache();

        var result = await cache.TryReadAsync("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_then_TryReadAsync_roundtrips()
    {
        var (cache, _) = CreateCache();
        var original = new TestPayload("roundtrip", 123);

        await cache.WriteAsync("key-1", original);
        var result = await cache.TryReadAsync("key-1");

        result.Should().NotBeNull();
        result!.Name.Should().Be("roundtrip");
        result.Value.Should().Be(123);
    }

    [Fact]
    public async Task ExistsAsync_returns_correct_status()
    {
        var (cache, _) = CreateCache();

        (await cache.ExistsAsync("key-1")).Should().BeFalse();
        await cache.WriteAsync("key-1", new TestPayload("test", 1));
        (await cache.ExistsAsync("key-1")).Should().BeTrue();
    }

    [Fact]
    public async Task GetOrFetchAsync_factory_exception_does_not_cache()
    {
        var (cache, store) = CreateCache();

        var act = () => cache.GetOrFetchAsync("key-1", (_, _) =>
            Task.FromException<TestPayload>(new InvalidOperationException("boom")));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await store.ExistsAsync("key-1")).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_overwrites_existing_entry()
    {
        var (cache, _) = CreateCache();
        await cache.WriteAsync("key-1", new TestPayload("first", 1));
        await cache.WriteAsync("key-1", new TestPayload("second", 2));

        var result = await cache.TryReadAsync("key-1");

        result!.Name.Should().Be("second");
        result.Value.Should().Be(2);
    }

    [Fact]
    public async Task Custom_key_mapper_is_used()
    {
        var store = new InMemorySnapshotStore();
        var cache = new WriteThroughCache<int, TestPayload>(
            store,
            new JsonTestSerializer(),
            key => $"prefix/{key}");

        await cache.WriteAsync(42, new TestPayload("mapped", 42));

        (await store.ExistsAsync("prefix/42")).Should().BeTrue();
    }

    // --- Test helpers ---

    private static (WriteThroughCache<string, TestPayload> Cache, InMemorySnapshotStore Store) CreateCache()
    {
        var store = new InMemorySnapshotStore();
        var cache = new WriteThroughCache<string, TestPayload>(
            store,
            new JsonTestSerializer(),
            key => key);
        return (cache, store);
    }

    private sealed record TestPayload(string Name, int Value);

    private sealed class JsonTestSerializer : ISnapshotSerializer<TestPayload>
    {
        public async Task SerializeAsync(Stream stream, TestPayload data, CancellationToken cancellationToken = default) =>
            await JsonSerializer.SerializeAsync(stream, data, cancellationToken: cancellationToken).ConfigureAwait(false);

        public async Task<TestPayload> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default) =>
            (await JsonSerializer.DeserializeAsync<TestPayload>(stream, cancellationToken: cancellationToken).ConfigureAwait(false))!;
    }
}
