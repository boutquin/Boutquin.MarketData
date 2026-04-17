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
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.MarketData.Normalization.Normalizers;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// Integration tests wiring the full DI-registered pipeline (AddMarketDataKernel)
/// with fake adapters returning canonical records — the same pattern used by
/// OptionPricing Example 15 but without live network calls.
/// </summary>
public sealed class FullPipelineTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"marketdata-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private ServiceProvider BuildKernel(Action<IServiceCollection> configureAdapters)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_tempDir, "snapshots"),
            rawDocumentDirectory: Path.Combine(_tempDir, "raw"),
            checkpointDirectory: Path.Combine(_tempDir, "checkpoints"),
            eventLogDirectory: Path.Combine(_tempDir, "events"));
        configureAdapters(services);
        return services.BuildServiceProvider();
    }

    // --- Simulated SOFR-like data (ScalarObservation via EconomicSeriesRequest) ---

    [Fact]
    public async Task Fetch_ScalarObservation_end_to_end_with_normalization()
    {
        // Arrange: fake adapter returns SOFR-like rate data in percent (like NY Fed API)
        var sofrData = new List<ScalarObservation>
        {
            new(new DateOnly(2026, 4, 7), 4.33m, "percent"),
            new(new DateOnly(2026, 4, 8), 4.34m, "percent"),
            new(new DateOnly(2026, 4, 9), 4.33m, "percent"),
        };

        var provenance = new DataProvenance(
            new ProviderCode("fake-nyfed"), "SOFR", LicenseType.Free, RetrievalMode.Api,
            FreshnessClass.EndOfDay, DateTimeOffset.UtcNow,
            "https://fake.nyfed.org/sofr");

        using var sp = BuildKernel(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<EconomicSeriesRequest, ScalarObservation>>(
                new FakeScalarAdapter("fake-nyfed", sofrData, provenance));
            svc.AddSingleton<INormalizer<ScalarObservation, ScalarObservation>>(
                new ScalarObservationNormalizer());
        });

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var range = new DateRange(new DateOnly(2026, 4, 7), new DateOnly(2026, 4, 9));
        var request = new EconomicSeriesRequest(new EconomicSeriesId("SOFR"), range);

        // Act
        var result = await pipeline.ExecuteAsync<EconomicSeriesRequest, ScalarObservation>(request)
            .ConfigureAwait(true);

        // Assert: records returned, normalized to decimal (percent → /100)
        result.Payload.Should().HaveCount(3);
        result.Payload[0].Value.Should().Be(0.0433m);
        result.Payload[1].Value.Should().Be(0.0434m);
        result.Payload[2].Value.Should().Be(0.0433m);
        result.Payload.Should().OnlyContain(o => o.Units == "decimal");

        // Provenance present
        result.Provenance.Should().NotBeEmpty();
        result.Provenance.Should().Contain(p => p.Provider == new ProviderCode("fake-nyfed"));

        // Coverage
        result.Coverage.ReturnedPoints.Should().Be(3);
        result.Coverage.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Fact]
    public async Task Second_call_serves_from_L1_cache()
    {
        var fetchCount = 0;
        var sofrData = new List<ScalarObservation>
        {
            new(new DateOnly(2026, 4, 9), 4.33m, "percent"),
        };
        var provenance = new DataProvenance(
            new ProviderCode("fake-nyfed"), "SOFR", LicenseType.Free, RetrievalMode.Api,
            FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, null);

        using var sp = BuildKernel(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<EconomicSeriesRequest, ScalarObservation>>(
                new FakeScalarAdapter("fake-nyfed", sofrData, provenance, () => fetchCount++));
            svc.AddSingleton<INormalizer<ScalarObservation, ScalarObservation>>(
                new ScalarObservationNormalizer());
        });

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var range = new DateRange(new DateOnly(2026, 4, 9), new DateOnly(2026, 4, 9));
        var request = new EconomicSeriesRequest(new EconomicSeriesId("SOFR"), range);

        // First call
        var r1 = await pipeline.ExecuteAsync<EconomicSeriesRequest, ScalarObservation>(request)
            .ConfigureAwait(true);
        fetchCount.Should().Be(1);

        // Second call — L1 hit
        var r2 = await pipeline.ExecuteAsync<EconomicSeriesRequest, ScalarObservation>(request)
            .ConfigureAwait(true);
        fetchCount.Should().Be(1); // adapter NOT called again
        r2.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Cache);
    }

    [Fact]
    public async Task L2_snapshot_persists_and_survives_new_pipeline()
    {
        var sofrData = new List<ScalarObservation>
        {
            new(new DateOnly(2026, 4, 10), 4.35m, "percent"),
        };
        var provenance = new DataProvenance(
            new ProviderCode("fake-nyfed"), "SOFR", LicenseType.Free, RetrievalMode.Api,
            FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, null);

        var range = new DateRange(new DateOnly(2026, 4, 10), new DateOnly(2026, 4, 10));
        var request = new EconomicSeriesRequest(new EconomicSeriesId("SOFR"), range);

        // Pipeline 1 — fetch and persist
        var fetchCount = 0;
        using (var sp1 = BuildKernel(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<EconomicSeriesRequest, ScalarObservation>>(
                new FakeScalarAdapter("fake-nyfed", sofrData, provenance, () => fetchCount++));
            svc.AddSingleton<INormalizer<ScalarObservation, ScalarObservation>>(
                new ScalarObservationNormalizer());
        }))
        {
            var p1 = sp1.GetRequiredService<IDataPipeline>();
            await p1.ExecuteAsync<EconomicSeriesRequest, ScalarObservation>(request)
                .ConfigureAwait(true);
        }

        fetchCount.Should().Be(1);

        // Pipeline 2 — new DI container, same disk directory. Adapter should NOT be called.
        using (var sp2 = BuildKernel(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<EconomicSeriesRequest, ScalarObservation>>(
                new FakeScalarAdapter("fake-nyfed", sofrData, provenance, () => fetchCount++));
            svc.AddSingleton<INormalizer<ScalarObservation, ScalarObservation>>(
                new ScalarObservationNormalizer());
        }))
        {
            var p2 = sp2.GetRequiredService<IDataPipeline>();
            var r2 = await p2.ExecuteAsync<EconomicSeriesRequest, ScalarObservation>(request)
                .ConfigureAwait(true);

            fetchCount.Should().Be(1); // L2 snapshot hit
            r2.Payload.Should().HaveCount(1);
            r2.Payload[0].Value.Should().Be(0.0435m); // normalized
            r2.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Snapshot);
        }
    }

    // --- Simulated yield curve data (YieldCurveQuote via YieldCurveQuoteRequest) ---

    [Fact]
    public async Task Fetch_YieldCurveQuotes_end_to_end()
    {
        // Arrange: fake adapter returns Treasury-like par yields
        var quotes = new List<YieldCurveQuote>
        {
            new("2Y", 4.21m),
            new("5Y", 3.98m),
            new("10Y", 4.12m),
            new("30Y", 4.45m),
        };

        var provenance = new DataProvenance(
            new ProviderCode("fake-treasury"), "USD-PAR", LicenseType.Free, RetrievalMode.Api,
            FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, null,
            new DateOnly(2026, 4, 10));

        using var sp = BuildKernel(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>>(
                new FakeYieldCurveAdapter("fake-treasury", quotes, provenance));
        });

        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = new YieldCurveQuoteRequest(new YieldCurveId("USD-PAR"), new DateOnly(2026, 4, 10));

        // Act
        var result = await pipeline.ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(request)
            .ConfigureAwait(true);

        // Assert
        result.Payload.Should().HaveCount(4);
        result.Payload.Should().Contain(q => q.Tenor == "10Y" && q.Rate == 4.12m);
        result.Provenance.Should().Contain(p => p.Provider == new ProviderCode("fake-treasury"));
        result.Provenance.Should().Contain(p => p.DataDate == new DateOnly(2026, 4, 10));
    }

    #region Test doubles

    private sealed class FakeScalarAdapter(
        string providerKey,
        IReadOnlyList<ScalarObservation> data,
        DataProvenance provenance,
        Action? onFetch = null)
        : IDataSourceAdapter<EconomicSeriesRequest, ScalarObservation>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new(providerKey);
        public int Priority => 100;
        public bool CanHandle(EconomicSeriesRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<ScalarObservation>>> FetchAsync(
            EconomicSeriesRequest request, CancellationToken cancellationToken = default)
        {
            onFetch?.Invoke();
            var coverage = new DataCoverage(data.Count, data.Count, 0, 1.0m);
            return Task.FromResult(new DataEnvelope<IReadOnlyList<ScalarObservation>>(
                data, coverage, [], [provenance]));
        }
    }

    private sealed class FakeYieldCurveAdapter(
        string providerKey,
        IReadOnlyList<YieldCurveQuote> data,
        DataProvenance provenance)
        : IDataSourceAdapter<YieldCurveQuoteRequest, YieldCurveQuote>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new(providerKey);
        public int Priority => 100;
        public bool CanHandle(YieldCurveQuoteRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<YieldCurveQuote>>> FetchAsync(
            YieldCurveQuoteRequest request, CancellationToken cancellationToken = default)
        {
            var coverage = new DataCoverage(data.Count, data.Count, 0, 1.0m);
            return Task.FromResult(new DataEnvelope<IReadOnlyList<YieldCurveQuote>>(
                data, coverage, [], [provenance]));
        }
    }

    #endregion
}
