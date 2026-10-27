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
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Catalog.Registry;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Catalog.Tests.Unit;

public sealed class SourceRegistryTests
{
    [Fact]
    public void All_returns_all_registered_adapters()
    {
        var registry = CreateRegistry("alpha", "beta");

        registry.All.Should().HaveCount(2);
    }

    [Fact]
    public void TryGetByKey_finds_adapter_case_insensitively()
    {
        // ProviderCode normalises input to lower-case on construction, so "Alpha" and "ALPHA"
        // resolve to the same key.
        var registry = CreateRegistry("Alpha");

        registry.TryGetByKey(new ProviderCode("ALPHA"), out var adapter).Should().BeTrue();
        adapter!.ProviderKey.Should().Be(new ProviderCode("Alpha"));
    }

    [Fact]
    public void TryGetByKey_returns_false_for_unknown_key()
    {
        var registry = CreateRegistry("alpha");

        registry.TryGetByKey(new ProviderCode("unknown"), out _).Should().BeFalse();
    }

    [Fact]
    public void GetHandlers_returns_adapters_that_can_handle_request()
    {
        var adapters = new[]
        {
            new StubAdapter("yes-1", canHandle: true),
            new StubAdapter("no-1", canHandle: false),
            new StubAdapter("yes-2", canHandle: true),
        };
        var registry = new SourceRegistry<StubRequest, string>(adapters);
        var request = new StubRequest("test", new DateRange(DateOnly.MinValue, DateOnly.MaxValue), DataFrequency.Daily);

        var handlers = registry.GetHandlers(request);

        handlers.Should().HaveCount(2);
        handlers.Select(h => h.ProviderKey)
            .Should().BeEquivalentTo(new[] { new ProviderCode("yes-1"), new ProviderCode("yes-2") });
    }

    [Fact]
    public void GetBy_filters_with_custom_predicate()
    {
        var registry = CreateRegistry("alpha", "beta", "gamma");

        var result = registry.GetBy(a => a.ProviderKey.Value.StartsWith("b", StringComparison.OrdinalIgnoreCase));

        result.Should().ContainSingle().Which.ProviderKey.Should().Be(new ProviderCode("beta"));
    }

    [Fact]
    public void Constructor_throws_on_duplicate_provider_keys()
    {
        var adapters = new[]
        {
            new StubAdapter("same"),
            new StubAdapter("SAME"),
        };

        var act = () => new SourceRegistry<StubRequest, string>(adapters);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Empty_registry_returns_empty_collections()
    {
        var registry = new SourceRegistry<StubRequest, string>(Array.Empty<IDataSourceAdapter<StubRequest, string>>());

        registry.All.Should().BeEmpty();
        registry.GetBy(_ => true).Should().BeEmpty();
    }

    // --- Test helpers ---

    private static SourceRegistry<StubRequest, string> CreateRegistry(params string[] providerKeys) =>
        new(providerKeys.Select(k => new StubAdapter(k)));

    private sealed record StubRequest(string DatasetKey, DateRange Range, DataFrequency Frequency) : IDataRequest;

    private sealed class StubAdapter : IDataSourceAdapter<StubRequest, string>
    {
        private readonly bool _canHandle;

        public StubAdapter(string providerKey, bool canHandle = true)
        {
            ProviderKey = new ProviderCode(providerKey);
            _canHandle = canHandle;
        }

        public ProviderCode ProviderKey { get; }

        public bool CanHandle(StubRequest request) => _canHandle;

        public Task<DataEnvelope<IReadOnlyList<string>>> FetchAsync(
            StubRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
