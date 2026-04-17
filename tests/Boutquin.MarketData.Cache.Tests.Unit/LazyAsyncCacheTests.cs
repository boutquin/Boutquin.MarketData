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
using Boutquin.MarketData.Cache.Concurrent;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit;

public sealed class LazyAsyncCacheTests
{
    [Fact]
    public async Task GetOrAddAsync_returns_factory_result()
    {
        using var cache = new LazyAsyncCache<string, int>();

        var result = await cache.GetOrAddAsync("key", (_, _) => Task.FromResult(42));

        result.Should().Be(42);
    }

    [Fact]
    public async Task GetOrAddAsync_calls_factory_only_once_for_same_key()
    {
        using var cache = new LazyAsyncCache<string, int>();
        var callCount = 0;

        var first = await cache.GetOrAddAsync("key", (_, _) => { Interlocked.Increment(ref callCount); return Task.FromResult(1); });
        var second = await cache.GetOrAddAsync("key", (_, _) => { Interlocked.Increment(ref callCount); return Task.FromResult(2); });

        first.Should().Be(1);
        second.Should().Be(1); // Same value — factory not called again
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrAddAsync_different_keys_call_factory_independently()
    {
        using var cache = new LazyAsyncCache<string, string>();

        var a = await cache.GetOrAddAsync("a", (k, _) => Task.FromResult($"value-{k}"));
        var b = await cache.GetOrAddAsync("b", (k, _) => Task.FromResult($"value-{k}"));

        a.Should().Be("value-a");
        b.Should().Be("value-b");
        cache.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetOrAddAsync_concurrent_callers_share_single_computation()
    {
        using var cache = new LazyAsyncCache<string, int>();
        var callCount = 0;
        var tcs = new TaskCompletionSource<int>();

        // Start 10 concurrent requests for the same key
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => cache.GetOrAddAsync("key", (_, _) =>
            {
                Interlocked.Increment(ref callCount);
                return tcs.Task;
            }))
            .ToList();

        // All 10 should be waiting on the same Task
        await Task.Delay(50); // Let all tasks register
        callCount.Should().Be(1);

        // Complete the single computation
        tcs.SetResult(99);
        var results = await Task.WhenAll(tasks);

        results.Should().AllBeEquivalentTo(99);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrAddAsync_evicts_faulted_entry_and_retries()
    {
        using var cache = new LazyAsyncCache<string, int>();
        var attempt = 0;

        // First call fails
        var act = () => cache.GetOrAddAsync("key", (_, _) =>
        {
            if (Interlocked.Increment(ref attempt) == 1)
            {
                return Task.FromException<int>(new InvalidOperationException("fail"));
            }
            return Task.FromResult(42);
        });

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Second call should retry (faulted entry evicted)
        var result = await cache.GetOrAddAsync("key", (_, _) => Task.FromResult(42));
        result.Should().Be(42);
    }

    [Fact]
    public async Task TryRemove_removes_existing_entry()
    {
        using var cache = new LazyAsyncCache<string, int>();
        await cache.GetOrAddAsync("key", (_, _) => Task.FromResult(1));

        cache.TryRemove("key").Should().BeTrue();
        cache.Count.Should().Be(0);
    }

    [Fact]
    public void TryRemove_returns_false_for_missing_key()
    {
        using var cache = new LazyAsyncCache<string, int>();

        cache.TryRemove("missing").Should().BeFalse();
    }

    [Fact]
    public async Task Clear_removes_all_entries()
    {
        using var cache = new LazyAsyncCache<string, int>();
        await cache.GetOrAddAsync("a", (_, _) => Task.FromResult(1));
        await cache.GetOrAddAsync("b", (_, _) => Task.FromResult(2));

        cache.Clear();

        cache.Count.Should().Be(0);
    }

    [Fact]
    public async Task Custom_comparer_is_used_for_key_lookup()
    {
        using var cache = new LazyAsyncCache<string, int>(StringComparer.OrdinalIgnoreCase);
        var callCount = 0;

        await cache.GetOrAddAsync("Key", (_, _) => { Interlocked.Increment(ref callCount); return Task.FromResult(1); });
        await cache.GetOrAddAsync("KEY", (_, _) => { Interlocked.Increment(ref callCount); return Task.FromResult(2); });

        callCount.Should().Be(1); // Same key via case-insensitive comparer
        cache.Count.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_clears_cache()
    {
        var cache = new LazyAsyncCache<string, int>();
        await cache.GetOrAddAsync("key", (_, _) => Task.FromResult(1));

        cache.Dispose();

        cache.Count.Should().Be(0);
    }
}
