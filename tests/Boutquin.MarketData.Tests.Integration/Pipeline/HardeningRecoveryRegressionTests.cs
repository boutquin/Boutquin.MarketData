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
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.DependencyInjection;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>Production-entry regressions captured during the kernel hardening verification.</summary>
public sealed class HardeningRecoveryRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"marketdata-independent-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ServiceProvider BuildHost(string slot, Action<IServiceCollection>? configure = null)
    {
        var root = Path.Combine(_root, slot);
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(root, "snapshots"),
            rawDocumentDirectory: Path.Combine(root, "raw"),
            checkpointDirectory: Path.Combine(root, "checkpoints"),
            eventLogDirectory: Path.Combine(root, "events"),
            timeSeriesDirectory: Path.Combine(root, "timeseries"),
            configurePipeline: options => options.UseTimeSeriesStore = true);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static DataEnvelope<IReadOnlyList<TRecord>> Envelope<TRecord>(
        IReadOnlyList<TRecord> records,
        string provider,
        string dataset,
        DataCoverage? coverage = null) =>
        new(
            records,
            coverage ?? new DataCoverage(records.Count, records.Count, 0, 1m),
            [],
            [new DataProvenance(
                new ProviderCode(provider), dataset, LicenseType.Free, RetrievalMode.Api,
                FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, $"https://{provider}.test/{dataset}")]);

    private sealed class DividendAdapter(
        string provider,
        int priority,
        Func<DividendHistoryRequest, bool> canHandle,
        Func<DividendHistoryRequest, DataEnvelope<IReadOnlyList<DividendPayment>>> response)
        : IDataSourceAdapter<DividendHistoryRequest, DividendPayment>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new(provider);
        public int Priority { get; } = priority;
        public int Calls { get; private set; }
        public bool CanHandle(DividendHistoryRequest request) => canHandle(request);
        public Task<DataEnvelope<IReadOnlyList<DividendPayment>>> FetchAsync(
            DividendHistoryRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(response(request));
        }
    }

    private sealed class FactorAdapter(
        Func<FactorSeriesRequest, IReadOnlyList<FactorObservation>> response,
        DataCoverage? coverage = null)
        : IDataSourceAdapter<FactorSeriesRequest, FactorObservation>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new("heldout-factors");
        public int Priority => 100;
        public int Calls { get; private set; }
        public bool CanHandle(FactorSeriesRequest request) => true;
        public Task<DataEnvelope<IReadOnlyList<FactorObservation>>> FetchAsync(
            FactorSeriesRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var rows = response(request);
            return Task.FromResult(Envelope(rows, ProviderKey.Value, request.DatasetKey, coverage));
        }
    }

    [Fact]
    public async Task MultiSymbolRestartRecoversProviderAndEmptySpanWithoutDispatch()
    {
        var from = new DateOnly(2025, 4, 7);
        var to = new DateOnly(2025, 4, 8);
        var request = new DividendHistoryRequest([new Symbol("JNK"), new Symbol("VNQ")], new DateRange(from, to));
        var adapter = new DividendAdapter(
            "heldout-dividends",
            100,
            _ => true,
            partition => Envelope<DividendPayment>(
                partition.Symbols[0].Ticker == "JNK"
                    ? [new DividendPayment(from, 0.19m, Currency.USD, DividendType.Unclassified)]
                    : [],
                "heldout-dividends",
                partition.DatasetKey));

        using (var writer = BuildHost(
            "multi-symbol-restart",
            services => services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(adapter)))
        {
            var live = await writer.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request);
            live.Payload.Should().ContainSingle(payment => payment.Amount == 0.19m);
            adapter.Calls.Should().Be(2, "the default policy must separately exercise both symbols");
        }

        using var reader = BuildHost("multi-symbol-restart");
        var replay = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request);
        replay.Payload.Should().ContainSingle(payment => payment.Amount == 0.19m);
        replay.Issues.Should().BeEmpty("the JNK provider attribution and VNQ known-empty result are durable");
    }

    [Fact]
    public async Task CrossProviderPrefixTailIsMergedLiveAndAfterRestart()
    {
        var day1 = new DateOnly(2025, 5, 5);
        var day2 = day1.AddDays(1);
        var day3 = day2.AddDays(1);
        var initial = new DividendHistoryRequest([new Symbol("VTI")], new DateRange(day1, day2));
        var extended = initial with { Range = new DateRange(day1, day3) };
        var primary = new DividendAdapter(
            "heldout-primary",
            100,
            partition => partition.Range.To <= day2,
            _ => Envelope(
                [
                    new DividendPayment(day1, 1.11m, Currency.USD, DividendType.Unclassified),
                    new DividendPayment(day2, 1.12m, Currency.USD, DividendType.Unclassified),
                ],
                "heldout-primary",
                initial.DatasetKey));
        var fallback = new DividendAdapter(
            "heldout-fallback",
            50,
            _ => true,
            _ => Envelope(
                [
                    new DividendPayment(day2, 9.12m, Currency.USD, DividendType.Unclassified),
                    new DividendPayment(day3, 1.13m, Currency.USD, DividendType.Unclassified),
                ],
                "heldout-fallback",
                initial.DatasetKey));

        using (var writer = BuildHost(
            "prefix-tail",
            services =>
            {
                services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(primary);
                services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(fallback);
            }))
        {
            var pipeline = writer.GetRequiredService<IDataPipeline>();
            _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(initial);
            var live = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(extended);
            live.Payload.Select(payment => payment.ExDate).Should().Equal(day1, day2, day3);
            live.Payload.Single(payment => payment.ExDate == day2).Amount.Should().Be(9.12m,
                "the provider-key order, not response timing, is the deterministic conflict rule");
            fallback.Calls.Should().Be(1, "the tail gap selects the fallback exactly once");
        }

        using var reader = BuildHost("prefix-tail");
        var replay = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(extended);
        replay.Payload.Select(payment => payment.ExDate).Should().Equal(day1, day2, day3);
        replay.Payload.Single(payment => payment.ExDate == day2).Amount.Should().Be(9.12m,
            "restart must apply the same provider-key conflict rule");
        replay.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task FanOutKnownEmptySubspanSurvivesRestartWithoutLiveFill()
    {
        var day1 = new DateOnly(2025, 6, 2);
        var day2 = day1.AddDays(1);
        var request1 = new FactorSeriesRequest(
            new FactorDatasetId("HeldOut_Factors"), new DateRange(day1, day1));
        var request2 = request1 with { Range = new DateRange(day2, day2) };
        var fullWindow = request1 with { Range = new DateRange(day1, day2) };
        var adapter = new FactorAdapter(request =>
            request.Range.From == day1
                ?
                [
                    new FactorObservation(day1, "Mkt-RF", 0.001m),
                    new FactorObservation(day1, "SMB", 0.002m),
                ]
                :
                [new FactorObservation(day2, "Mkt-RF", 0.003m)]);

        using (var writer = BuildHost(
            "fanout-known-empty-restart",
            services => services.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(adapter)))
        {
            var pipeline = writer.GetRequiredService<IDataPipeline>();
            _ = await pipeline.ExecuteAsync<FactorSeriesRequest, FactorObservation>(request1);
            var second = await pipeline.ExecuteAsync<FactorSeriesRequest, FactorObservation>(request2);
            second.Payload.Should().ContainSingle(row => row.FactorName == "Mkt-RF");
            adapter.Calls.Should().Be(2);
        }

        using var reader = BuildHost("fanout-known-empty-restart");
        var replay = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<FactorSeriesRequest, FactorObservation>(fullWindow);
        replay.Payload.Should().HaveCount(3, "SMB has one durable day-one row and a known-empty day-two tail");
        replay.Issues.Should().BeEmpty("the per-factor known-empty tail must survive host restart");
    }

    [Fact]
    public async Task FanOutPartialResponseDoesNotPersistMissingFactorAsKnownEmpty()
    {
        var day1 = new DateOnly(2025, 7, 7);
        var day2 = day1.AddDays(1);
        var initial = new FactorSeriesRequest(
            new FactorDatasetId("Partial_Factors"), new DateRange(day1, day1));
        var missingDay = initial with { Range = new DateRange(day2, day2) };
        var fullWindow = initial with { Range = new DateRange(day1, day2) };
        var seed = new FactorAdapter(_ =>
        [
            new FactorObservation(day1, "Mkt-RF", 0.001m),
            new FactorObservation(day1, "SMB", 0.002m),
        ]);
        using (var host = BuildHost("partial-factor", services =>
            services.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(seed)))
        {
            _ = await host.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<FactorSeriesRequest, FactorObservation>(initial);
        }

        var partial = new FactorAdapter(
            _ => [new FactorObservation(day2, "Mkt-RF", 0.003m)],
            new DataCoverage(2, 1, 1, 0.5m));
        using (var host = BuildHost("partial-factor", services =>
            services.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(partial)))
        {
            _ = await host.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<FactorSeriesRequest, FactorObservation>(missingDay);
            partial.Calls.Should().Be(1);
        }

        var repair = new FactorAdapter(_ =>
        [
            new FactorObservation(day2, "Mkt-RF", 0.003m),
            new FactorObservation(day2, "SMB", 0.004m),
        ]);
        using var reader = BuildHost("partial-factor", services =>
            services.AddSingleton<IDataSourceAdapter<FactorSeriesRequest, FactorObservation>>(repair));
        var resolved = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<FactorSeriesRequest, FactorObservation>(fullWindow);
        repair.Calls.Should().Be(1, "a Partial source response cannot certify the missing factor as empty");
        resolved.Payload.Should().HaveCount(4);
        resolved.Payload.Should().Contain(row => row.Date == day2 && row.FactorName == "SMB" && row.Value == 0.004m);
    }
}
