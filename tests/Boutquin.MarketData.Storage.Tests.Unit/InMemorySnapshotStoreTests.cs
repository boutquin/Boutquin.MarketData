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

using Boutquin.MarketData.Storage.InMemory;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit;

public sealed class InMemorySnapshotStoreTests
{
    [Fact]
    public async Task ExistsAsync_returns_false_for_missing_key()
    {
        var store = new InMemorySnapshotStore();

        var exists = await store.ExistsAsync("nonexistent");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_then_ExistsAsync_returns_true()
    {
        var store = new InMemorySnapshotStore();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("test-data"));

        await store.WriteAsync("key-1", content);

        (await store.ExistsAsync("key-1")).Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_returns_null_for_missing_key()
    {
        var store = new InMemorySnapshotStore();

        var result = await store.ReadAsync("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_then_ReadAsync_returns_original_content()
    {
        var store = new InMemorySnapshotStore();
        var payload = "hello-snapshot"u8.ToArray();
        using var content = new MemoryStream(payload);

        await store.WriteAsync("key-1", content);
        using var result = await store.ReadAsync("key-1");

        result.Should().NotBeNull();
        using var reader = new MemoryStream();
        await result!.CopyToAsync(reader);
        reader.ToArray().Should().BeEquivalentTo(payload);
    }

    [Fact]
    public async Task WriteAsync_overwrites_existing_snapshot()
    {
        var store = new InMemorySnapshotStore();
        using var first = new MemoryStream(Encoding.UTF8.GetBytes("first"));
        using var second = new MemoryStream(Encoding.UTF8.GetBytes("second"));

        await store.WriteAsync("key-1", first);
        await store.WriteAsync("key-1", second);

        using var result = await store.ReadAsync("key-1");
        using var reader = new MemoryStream();
        await result!.CopyToAsync(reader);
        Encoding.UTF8.GetString(reader.ToArray()).Should().Be("second");
    }

    [Fact]
    public async Task Keys_are_case_sensitive()
    {
        var store = new InMemorySnapshotStore();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("data"));

        await store.WriteAsync("My-Key", content);

        (await store.ExistsAsync("My-Key")).Should().BeTrue();
        (await store.ExistsAsync("my-key")).Should().BeFalse();
        (await store.ExistsAsync("MY-KEY")).Should().BeFalse();
    }

    [Fact]
    public async Task ReadAsync_returns_independent_streams()
    {
        var store = new InMemorySnapshotStore();
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("shared"));

        await store.WriteAsync("key-1", content);

        using var read1 = await store.ReadAsync("key-1");
        using var read2 = await store.ReadAsync("key-1");

        // Both streams should be independently readable.
        read1!.Position.Should().Be(0);
        read2!.Position.Should().Be(0);

        // Reading one should not affect the other.
        read1.ReadByte();
        read2.Position.Should().Be(0);
    }

    [Fact]
    public async Task Multiple_keys_are_independent()
    {
        var store = new InMemorySnapshotStore();
        using var content1 = new MemoryStream(Encoding.UTF8.GetBytes("alpha"));
        using var content2 = new MemoryStream(Encoding.UTF8.GetBytes("beta"));

        await store.WriteAsync("key-a", content1);
        await store.WriteAsync("key-b", content2);

        (await store.ExistsAsync("key-a")).Should().BeTrue();
        (await store.ExistsAsync("key-b")).Should().BeTrue();
        (await store.ExistsAsync("key-c")).Should().BeFalse();
    }
}
