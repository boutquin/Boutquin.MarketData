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
using Boutquin.MarketData.Testing.Builders;
using Boutquin.MarketData.Testing.Fakes;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Testing.Tests.Unit;

public sealed class FakeDataSourceAdapterTests
{
    [Fact]
    public async Task FetchAsync_returns_factory_result()
    {
        var envelope = new EnvelopeBuilder<IReadOnlyList<string>>()
            .WithPayload(new List<string> { "a", "b" })
            .WithCoverage(2, 2, 0, 1.0m)
            .Build();

        var adapter = new FakeDataSourceAdapter<StubRequest, string>(new ProviderCode("test"), _ => envelope);
        var request = MakeRequest("ds-1");

        var result = await adapter.FetchAsync(request);

        result.Payload.Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchAsync_records_invocations()
    {
        var envelope = new EnvelopeBuilder<IReadOnlyList<string>>()
            .WithPayload(Array.Empty<string>().ToList())
            .Build();

        var adapter = new FakeDataSourceAdapter<StubRequest, string>(new ProviderCode("test"), _ => envelope);

        await adapter.FetchAsync(MakeRequest("a"));
        await adapter.FetchAsync(MakeRequest("b"));

        adapter.Invocations.Should().HaveCount(2);
        adapter.Invocations[0].DatasetKey.Should().Be("a");
        adapter.Invocations[1].DatasetKey.Should().Be("b");
    }

    [Fact]
    public void CanHandle_defaults_to_true()
    {
        var adapter = new FakeDataSourceAdapter<StubRequest, string>(
            new ProviderCode("test"), _ => null!);

        adapter.CanHandle(MakeRequest("x")).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_uses_custom_predicate()
    {
        var adapter = new FakeDataSourceAdapter<StubRequest, string>(
            new ProviderCode("test"), _ => null!, canHandle: r => r.DatasetKey == "yes");

        adapter.CanHandle(MakeRequest("yes")).Should().BeTrue();
        adapter.CanHandle(MakeRequest("no")).Should().BeFalse();
    }

    [Fact]
    public void ProviderKey_returns_configured_value()
    {
        var adapter = new FakeDataSourceAdapter<StubRequest, string>(new ProviderCode("my-provider"), _ => null!);

        adapter.ProviderKey.Should().Be(new ProviderCode("my-provider"));
    }

    private static StubRequest MakeRequest(string datasetKey) =>
        new(datasetKey, new DateRange(DateOnly.MinValue, DateOnly.MaxValue), DataFrequency.Daily);

    private sealed record StubRequest(string DatasetKey, DateRange Range, DataFrequency Frequency) : IDataRequest;
}
