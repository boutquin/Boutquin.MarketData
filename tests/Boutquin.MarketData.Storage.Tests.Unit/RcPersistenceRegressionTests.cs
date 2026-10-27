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
using Boutquin.MarketData.Storage.RawDocuments;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.Storage.Infrastructure.ObjectStore;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit;

public sealed class RcPersistenceRegressionTests
{
    [Fact]
    public async Task Invalid_utf16_cannot_replace_an_existing_raw_document()
    {
        var backend = new InMemoryObjectStore();
        var store = new FileRawDocumentStore(backend);
        await store.SaveAsync("k", "original");
        await Assert.ThrowsAsync<EncoderFallbackException>(() => store.SaveAsync("k", new string((char)0xd800, 1)));
        (await store.LoadAsync("k")).Should().Be("original");
    }

    [Fact]
    public async Task Corrupt_utf8_is_reported_instead_of_returning_replacement_text()
    {
        var backend = new InMemoryObjectStore();
        using var bytes = new MemoryStream([0xc3, 0x28]);
        await backend.WriteAsync("006b.raw-v2", bytes);
        var store = new FileRawDocumentStore(backend);
        await Assert.ThrowsAsync<DecoderFallbackException>(() => store.LoadAsync("k"));
    }

    [Theory]
    [InlineData("Provider/observations", "provider/observations")]
    [InlineData("Provider/observations", "Provider/Observations")]
    public async Task Legacy_snapshot_identity_requires_ordinal_match_in_every_component(string original, string alternate)
    {
        var root = Path.Combine(Path.GetTempPath(), "md-rc-identity-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(root, original);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "original");
            var store = new FileSnapshotStore(root);
            (await store.ExistsAsync(alternate)).Should().BeFalse();
            await using var absent = await store.ReadAsync(alternate);
            absent.Should().BeNull();
            (await store.ExistsAsync(original)).Should().BeTrue();
            await using var present = await store.ReadAsync(original);
            using var reader = new StreamReader(present!);
            (await reader.ReadToEndAsync()).Should().Be("original");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    [Theory]
    [InlineData(1)]
    [InlineData(10000)]
    public async Task Preupgrade_hashed_snapshot_and_raw_v2_remain_readable_with_new_writes_taking_precedence(int keyLength)
    {
        var root = Path.Combine(Path.GetTempPath(), "md-rc-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "%snapshots-v1"));
            var key = new string('k', keyLength);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            await File.WriteAllTextAsync(Path.Combine(root, "%snapshots-v1", hash), "old snapshot");
            await File.WriteAllTextAsync(Path.Combine(root, "006b.raw-v2"), "old raw");
            var snapshots = new FileSnapshotStore(root);
            var raw = new FileRawDocumentStore(root);
            (await snapshots.ExistsAsync(key)).Should().BeTrue();
            await using (var stream = await snapshots.ReadAsync(key))
            using (var reader = new StreamReader(stream!))
            {
                (await reader.ReadToEndAsync()).Should().Be("old snapshot");
            }
            (await raw.LoadAsync("k")).Should().Be("old raw");
            using var replacement = new MemoryStream("new snapshot"u8.ToArray());
            await snapshots.WriteAsync(key, replacement);
            await raw.SaveAsync("k", "new raw");
            await using var updated = await snapshots.ReadAsync(key);
            using var updatedReader = new StreamReader(updated!);
            (await updatedReader.ReadToEndAsync()).Should().Be("new snapshot");
            (await raw.LoadAsync("k")).Should().Be("new raw");
            (await File.ReadAllTextAsync(Path.Combine(root, "006b.raw-v2"))).Should().Be("old raw");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

}
