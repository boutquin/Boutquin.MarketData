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
using Boutquin.MarketData.Storage.RawDocuments;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.Storage.Domain.Interfaces.ObjectStore;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit;

public sealed class RcPersistenceGuardRegressionTests
{
    [Fact]
    public void Injected_object_stores_reject_null_at_construction()
    {
        Assert.Throws<ArgumentNullException>(() => new FileRawDocumentStore((IObjectStore)null!)).ParamName.Should().Be("store");
        Assert.Throws<ArgumentNullException>(() => new FileSnapshotStore((IObjectStore)null!)).ParamName.Should().Be("store");
    }

    [Theory]
    [InlineData("hierarchical-file")]
    [InlineData("hierarchical-directory")]
    [InlineData("hashed-file")]
    [InlineData("raw-file")]
    [InlineData("raw-directory")]
    public async Task Legacy_descendant_links_cannot_expose_files_outside_the_root(string layout)
    {
        var parent = Path.Combine(Path.GetTempPath(), "md-rc-links-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parent, "store");
        var outside = Path.Combine(parent, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var secret = Path.Combine(outside, "secret");
        await File.WriteAllTextAsync(secret, "outside content");
        try
        {
            var key = "Provider/observations";
            if (layout == "hierarchical-file")
            {
                Directory.CreateDirectory(Path.Combine(root, "Provider"));
                File.CreateSymbolicLink(Path.Combine(root, "Provider", "observations"), secret);
            }
            else if (layout == "hierarchical-directory")
            {
                await File.WriteAllTextAsync(Path.Combine(outside, "observations"), "outside content");
                Directory.CreateSymbolicLink(Path.Combine(root, "Provider"), outside);
            }
            else if (layout == "hashed-file")
            {
                var directory = Path.Combine(root, "%snapshots-v1");
                Directory.CreateDirectory(directory);
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
                File.CreateSymbolicLink(Path.Combine(directory, hash), secret);
            }
            else if (layout == "raw-file")
            {
                key = "k";
                File.CreateSymbolicLink(Path.Combine(root, "006b.raw-v2"), secret);
            }
            else
            {
                key = new string('k', 48) + "z";
                await File.WriteAllTextAsync(Path.Combine(outside, "007a.raw-v2"), "outside content");
                Directory.CreateSymbolicLink(Path.Combine(root, string.Concat(Enumerable.Repeat("006b", 48))), outside);
            }

            if (layout == "raw-directory")
            {
                // The qualified backend rejects this ancestor before legacy fallback is reached.
                await Assert.ThrowsAsync<ArgumentException>(() => new FileRawDocumentStore(root).LoadAsync(key));
            }
            else if (layout.StartsWith("raw-", StringComparison.Ordinal))
            {
                (await new FileRawDocumentStore(root).LoadAsync(key)).Should().BeNull();
            }
            else
            {
                var store = new FileSnapshotStore(root);
                (await store.ExistsAsync(key)).Should().BeFalse();
                await using var stream = await store.ReadAsync(key);
                stream.Should().BeNull();
            }

            (await File.ReadAllTextAsync(secret)).Should().Be("outside content");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
