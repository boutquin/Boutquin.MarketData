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
using Boutquin.MarketData.Storage.RawDocuments;
using Boutquin.Storage.Infrastructure.ObjectStore;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit.RawDocuments;

[Trait("Tier", "Unit")]
public sealed class FileRawDocumentStoreTests
{
    [Theory]
    [InlineData("\uFEFFpayload-\uFEFF-middle")]
    [InlineData("\uFEFF\uFEFFpayload")]
    [InlineData("payload-\uFEFF-middle")]
    [InlineData("")]
    public async Task Saved_content_is_preserved_exactly_after_reopen(string content)
    {
        var backing = new InMemoryObjectStore();
        var writer = new FileRawDocumentStore(backing);
        await writer.SaveAsync("raw/content", content);

        var reader = new FileRawDocumentStore(backing);
        (await reader.LoadAsync("raw/content")).Should().Be(content,
            "raw replay must preserve content characters, including a leading U+FEFF");
    }

    [Theory]
    [InlineData("provider/ABC+DEF/2026", "provider/ABC_DEF/2026")]
    [InlineData("provider/a\\b/range", "provider/a/b/range")]
    public async Task Distinct_keys_preserve_both_payloads_after_reopen(string firstKey, string secondKey)
    {
        var backing = new InMemoryObjectStore();
        var writer = new FileRawDocumentStore(backing);
        await writer.SaveAsync(firstKey, "first");
        await writer.SaveAsync(secondKey, "second");

        var reader = new FileRawDocumentStore(backing);
        (await reader.LoadAsync(firstKey)).Should().Be("first", "distinct accepted keys identify distinct documents");
        (await reader.LoadAsync(secondKey)).Should().Be("second");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/../escape")]
    [InlineData("a\\..\\escape")]
    [InlineData("a//b")]
    [InlineData("/absolute")]
    [InlineData("C:drive")]
    public async Task Invalid_keys_are_rejected_before_writing(string key)
    {
        var store = new FileRawDocumentStore(new InMemoryObjectStore());
        var write = () => store.SaveAsync(key, "payload");
        await write.Should().ThrowAsync<ArgumentException>();
    }
}
