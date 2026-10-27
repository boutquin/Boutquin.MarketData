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
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.Keys;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

public sealed class RequestCacheIdentityTests : IDisposable
{
    private static readonly DateRange s_range = new(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "request-identity-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Price_pipeline_preserves_distinct_opaque_and_structured_identities(bool listCollision)
    {
        var first = new PriceHistoryRequest([new Symbol(listCollision ? "A_B" : "A/XNAS")], s_range);
        var second = listCollision
            ? new PriceHistoryRequest([new Symbol("A"), new Symbol("B")], s_range)
            : new PriceHistoryRequest([new Symbol("A")], s_range) { Exchange = ExchangeCode.XNAS };
        await AssertSeparateCacheEntries<PriceHistoryRequest, Bar>(first, second,
            [new Bar(s_range.From, 111m, 111m, 111m, 111m, 111m, 1)],
            [new Bar(s_range.From, 222m, 222m, 222m, 222m, 222m, 1)]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Metadata_pipeline_preserves_distinct_opaque_and_structured_identities(bool listCollision)
    {
        var first = new InstrumentMetadataRequest([new Symbol(listCollision ? "A_B" : "A/XNAS")]);
        var second = listCollision
            ? new InstrumentMetadataRequest([new Symbol("A"), new Symbol("B")])
            : new InstrumentMetadataRequest([new Symbol("A")]) { Exchange = ExchangeCode.XNAS };
        await AssertSeparateCacheEntries<InstrumentMetadataRequest, InstrumentMetadata>(first, second,
            [new InstrumentMetadata(first.Symbols[0], "first", ExchangeCode.XNYS, AssetClassCode.Equities, null, null, null)],
            [new InstrumentMetadata(second.Symbols[0], "second", ExchangeCode.XNAS, AssetClassCode.Equities, null, null, null)]);
    }

    [Theory]
    [InlineData("economic")]
    [InlineData("curve")]
    [InlineData("overnight")]
    [InlineData("option")]
    [InlineData("futures")]
    [InlineData("factor")]
    public void Scalar_request_identifier_cannot_alias_venue_suffix(string family)
    {
        IDataRequest Request(string identifier, ExchangeCode? exchange) => family switch
        {
            "economic" => new EconomicSeriesRequest(new EconomicSeriesId(identifier), s_range) { Exchange = exchange },
            "curve" => new YieldCurveQuoteRequest(new YieldCurveId(identifier), s_range.From) { Exchange = exchange },
            "overnight" => new OvernightFixingRequest(new BenchmarkName(identifier), s_range) { Exchange = exchange },
            "option" => new OptionChainRequest(identifier, s_range) { Exchange = exchange },
            "futures" => new FuturesSettlementRequest(new FuturesProductCode(identifier), s_range) { Exchange = exchange },
            "factor" => new FactorSeriesRequest(new FactorDatasetId(identifier), s_range) { Exchange = exchange },
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };

        Request("A/XNAS", null).DatasetKey.Should().NotBe(Request("A", ExchangeCode.XNAS).DatasetKey);
        Request("A", null).DatasetKey.Should().Be("A");
    }

    private async Task AssertSeparateCacheEntries<TRequest, TRecord>(
        TRequest first, TRequest second, IReadOnlyList<TRecord> firstPayload, IReadOnlyList<TRecord> secondPayload)
        where TRequest : IDataRequest
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(snapshotDirectory: Path.Combine(_root, "snapshots"),
            rawDocumentDirectory: Path.Combine(_root, "raw"), checkpointDirectory: Path.Combine(_root, "checkpoints"),
            eventLogDirectory: Path.Combine(_root, "events"), timeSeriesDirectory: Path.Combine(_root, "series"));
        await using var provider = services.BuildServiceProvider();
        var cache = provider.GetRequiredService<ILocalCache>();
        static string Key(TRequest request) => CacheKeyBuilder.FromDateRange(
            $"pipeline-v2/{typeof(TRecord).Name}/{request.Frequency}", request.DatasetKey, request.Range.From, request.Range.To);
        await cache.SetAsync(Key(first), new DataEnvelope<IReadOnlyList<TRecord>>(firstPayload, new(1, 1, 0, 1m), [], []), TimeSpan.FromMinutes(1));
        await cache.SetAsync(Key(second), new DataEnvelope<IReadOnlyList<TRecord>>(secondPayload, new(1, 1, 0, 1m), [], []), TimeSpan.FromMinutes(1));
        var pipeline = provider.GetRequiredService<IDataPipeline>();
        var firstResult = await pipeline.ExecuteAsync<TRequest, TRecord>(first);
        var secondResult = await pipeline.ExecuteAsync<TRequest, TRecord>(second);
        firstResult.Payload.Should().Equal(firstPayload);
        secondResult.Payload.Should().Equal(secondPayload);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
