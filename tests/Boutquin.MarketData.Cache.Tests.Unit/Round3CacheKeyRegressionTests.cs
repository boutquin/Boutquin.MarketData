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
using System.Security.Cryptography;
using System.Text;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.Keys;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.Storage.Infrastructure.ObjectStore;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit;

public sealed class Round3CacheKeyRegressionTests
{
    [Fact]
    public async Task Long_and_colliding_shape_keys_retain_independent_payloads()
    {
        var root = Path.Combine(Path.GetTempPath(), "snapshot-identity-" + Guid.NewGuid().ToString("N"));
        try
        {
            string[] keys =
            [
                CacheKeyBuilder.FromSegments("p", [new string('a', 10000)]),
                CacheKeyBuilder.FromSegments("p", ["a/b", "c"]),
                CacheKeyBuilder.FromSegments("p", ["a", "b/c"]),
                CacheKeyBuilder.FromDateRange("a/b", "c", null, null),
                CacheKeyBuilder.FromDateRange("a", "b/c", null, null),
                CacheKeyBuilder.FromDateRange("p", new string('z', 10000), null, null),
                "short", "SHORT"
            ];
            var store = new FileSnapshotStore(root);
            for (var i = 0; i < keys.Length; i++)
            {
                using var payload = new MemoryStream(Encoding.UTF8.GetBytes($"payload-{i}"));
                await store.WriteAsync(keys[i], payload);
            }

            store = new FileSnapshotStore(root);
            for (var i = 0; i < keys.Length; i++)
            {
                await using var stream = await store.ReadAsync(keys[i]);
                stream.Should().NotBeNull();
                using var reader = new StreamReader(stream!);
                (await reader.ReadToEndAsync()).Should().Be($"payload-{i}");
            }

            var absent = CacheKeyBuilder.FromSegments("p", [new string('b', 10000)]);
            (await store.ExistsAsync(absent)).Should().BeFalse();
            (await store.ReadAsync(absent)).Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Legacy_paths_remain_readable_and_new_writes_take_precedence_without_aliasing()
    {
        var root = Path.Combine(Path.GetTempPath(), "snapshot-legacy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var key = CacheKeyBuilder.FromDateRange("legacy", "AAPL", null, null);
            var digestShapedKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            var legacy = new FileSystemObjectStore(root);
            using var oldPayload = new MemoryStream("old"u8.ToArray());
            await legacy.WriteAsync(key, oldPayload);
            using var independentPayload = new MemoryStream("independent"u8.ToArray());
            await legacy.WriteAsync(digestShapedKey, independentPayload);

            var store = new FileSnapshotStore(root);
            (await store.ExistsAsync(key)).Should().BeTrue();
            await using (var old = await store.ReadAsync(key))
            using (var reader = new StreamReader(old!))
            {
                (await reader.ReadToEndAsync()).Should().Be("old");
            }

            using var replacement = new MemoryStream("new"u8.ToArray());
            await store.WriteAsync(key, replacement);
            store = new FileSnapshotStore(root);
            await using var updated = await store.ReadAsync(key);
            using var updatedReader = new StreamReader(updated!);
            (await updatedReader.ReadToEndAsync()).Should().Be("new");
            await using var independent = await store.ReadAsync(digestShapedKey);
            using var independentReader = new StreamReader(independent!);
            (await independentReader.ReadToEndAsync()).Should().Be("independent");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("a//b")]
    [InlineData("a/./b")]
    [InlineData("%snapshots-v1/file")]
    [InlineData("non-ascii-é")]
    public async Task Root_backed_store_rejects_invalid_logical_keys(string key)
    {
        var root = Path.Combine(Path.GetTempPath(), "snapshot-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileSnapshotStore(root);
            using var payload = new MemoryStream([1]);
            await Assert.ThrowsAsync<ArgumentException>(() => store.WriteAsync(key, payload));
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadAsync(key));
            await Assert.ThrowsAsync<ArgumentException>(() => store.ExistsAsync(key));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Injected_store_receives_logical_keys_unchanged()
    {
        var backend = new InMemoryObjectStore();
        var store = new FileSnapshotStore(backend);
        var key = CacheKeyBuilder.FromSegments("p", [new string('x', 10000)]);
        using var payload = new MemoryStream([1, 2, 3]);
        await store.WriteAsync(key, payload);
        (await backend.ExistsAsync(key)).Should().BeTrue();
        (await store.ExistsAsync(key)).Should().BeTrue();
        await using var stream = await store.ReadAsync(key);
        stream.Should().NotBeNull();
    }

    [Fact]
    public async Task Multi_symbol_key_round_trips_through_real_store_after_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "snapshot-key-" + Guid.NewGuid().ToString("N"));
        try
        {
            var request = new DividendHistoryRequest(
                Enumerable.Range(0, 20).Select(i => new Symbol($"S{i:D3}")).ToArray(),
                new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)));
            var key = CacheKeyBuilder.FromDateRange("pipeline-v2/DividendPayment/Daily",
                request.DatasetKey, request.Range.From, request.Range.To);
            var store = new FileSnapshotStore(root);
            using var payload = new MemoryStream(Encoding.UTF8.GetBytes("batch-20"));

            var exception = await Record.ExceptionAsync(() => store.WriteAsync(key, payload));

            exception.Should().BeNull("an accepted batch must fit the physical snapshot layout");
            var reopened = new FileSnapshotStore(root);
            (await reopened.ExistsAsync(key)).Should().BeTrue();
            await using var result = await reopened.ReadAsync(key);
            result.Should().NotBeNull();
            using var reader = new StreamReader(result!);
            (await reader.ReadToEndAsync()).Should().Be("batch-20");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
