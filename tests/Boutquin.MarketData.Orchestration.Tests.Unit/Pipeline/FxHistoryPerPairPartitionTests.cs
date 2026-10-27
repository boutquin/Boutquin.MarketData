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

using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.Memory;
using Boutquin.MarketData.Cache.Policies;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.Calendars;
using Boutquin.MarketData.Orchestration.Coverage;
using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.MarketData.Storage.Checkpointing;
using Boutquin.MarketData.Storage.Events;
using Boutquin.MarketData.Storage.InMemory;
using Boutquin.Storage.Infrastructure.Events;
using Boutquin.Storage.Infrastructure.Streaming.Checkpointing;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Boutquin.MarketData.Orchestration.Tests.Unit.Pipeline;

// AC-23c (M-2.6 FX normalization, §2.5.1 / Q15). Once QuoteCurrency leaves the FxRate body, a
// multi-pair FxHistoryRequest can no longer be demuxed by a row field. ExecutePartitionedAsync
// runs each pair through the full fetch path and returns one DataEnvelope<FxRate> per
// FxRateDatasetKey, so the consumer keys on the dataset key — where the pair identity now lives.
public sealed class FxHistoryPerPairPartitionTests
{
    private static readonly DateRange s_range = new(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5));

    // Distinct rate per pair so each partition's envelope is identifiable by value as well as by key.
    private static readonly IReadOnlyDictionary<FxPair, decimal> s_ratePerPair = new Dictionary<FxPair, decimal>
    {
        [new FxPair(Currency.EUR, Currency.USD)] = 1.0845m,
        [new FxPair(Currency.EUR, Currency.GBP)] = 0.8512m,
        [new FxPair(Currency.EUR, Currency.JPY)] = 165.30m,
    };

    private sealed class FakeFxAdapter : IDataSourceAdapter<FxHistoryRequest, FxRate>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new("frankfurter");
        public int Priority => 100;
        public bool CanHandle(FxHistoryRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<FxRate>>> FetchAsync(
            FxHistoryRequest request, CancellationToken cancellationToken = default)
        {
            // The pipeline splits the multi-pair batch into single-pair sub-requests before this
            // adapter is hit, so each fetch sees exactly one pair and returns that pair's rate.
            request.Pairs.Should().HaveCount(1, "ExecutePartitionedAsync must split the batch per pair before fetch");
            var pair = request.Pairs[0];
            var rate = s_ratePerPair[pair];

            var records = new List<FxRate> { new(request.Range.From, rate) };
            var provenance = new DataProvenance(
                ProviderKey, pair.ToString(), LicenseType.Free, RetrievalMode.Api,
                FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, null);

            return Task.FromResult(new DataEnvelope<IReadOnlyList<FxRate>>(
                records,
                new DataCoverage(1, 1, 0, 1.0m),
                [],
                [provenance]));
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private static DataPipeline BuildPipeline()
    {
        var clock = new FakeClock();
        var l1 = new ExactOnceMemoryCache();
        var snapshotStore = new InMemorySnapshotStore();
        var checkpointStore = new IngestionCheckpointStore(new InMemoryCheckpointStore());
        var eventLog = new IngestionEventLog(new InMemoryEventLog<IngestionEvent>());
        var stalenessPolicy = new SnapshotStalenessPolicy(TimeSpan.FromHours(24));
        var coverageCalc = new CoverageCalculator(new WeekendOnlyCalendar());
        var opts = Options.Create(new DataPipelineOptions());

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDataSourceAdapter<FxHistoryRequest, FxRate>>(new FakeFxAdapter());
        var sp = services.BuildServiceProvider();

        return new DataPipeline(
            l1, snapshotStore, checkpointStore, eventLog,
            stalenessPolicy, coverageCalc, opts, sp, clock,
            NullLogger<DataPipeline>.Instance);
    }

    [Fact]
    public async Task MultiPair_FxHistory_ReturnsOneEnvelopePerDatasetKey()
    {
        var pipeline = BuildPipeline();
        var builder = new FxRateDatasetKeyBuilder("frankfurter");

        var eurUsd = new FxPair(Currency.EUR, Currency.USD);
        var eurGbp = new FxPair(Currency.EUR, Currency.GBP);
        var eurJpy = new FxPair(Currency.EUR, Currency.JPY);

        var request = new FxHistoryRequest([eurUsd, eurGbp, eurJpy], s_range);

        var result = await pipeline.ExecutePartitionedAsync<FxHistoryRequest, FxRate, FxPair, FxRateDatasetKey>(
            request,
            splitForPartition: (req, pair) => req with { Pairs = [pair] },
            buildDatasetKey: builder.BuildForPair,
            CancellationToken.None).ConfigureAwait(true);

        // Three pairs → three envelopes, one per FxRateDatasetKey.
        result.Should().HaveCount(3);

        var eurUsdKey = new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.USD, DataFrequency.Daily);
        var eurGbpKey = new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.GBP, DataFrequency.Daily);
        var eurJpyKey = new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.JPY, DataFrequency.Daily);

        result.Should().ContainKeys(eurUsdKey, eurGbpKey, eurJpyKey);

        // Each envelope holds ONLY that pair's rate — demux is by dataset key, not by a row field.
        result[eurUsdKey].Payload.Should().ContainSingle().Which.Rate.Should().Be(1.0845m);
        result[eurGbpKey].Payload.Should().ContainSingle().Which.Rate.Should().Be(0.8512m);
        result[eurJpyKey].Payload.Should().ContainSingle().Which.Rate.Should().Be(165.30m);
    }
}
