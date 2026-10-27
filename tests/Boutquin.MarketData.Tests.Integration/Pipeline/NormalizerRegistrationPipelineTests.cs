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
using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.MarketData.Testing.Fakes;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// AC-2.1 (feature-kernel-normalizer-registration.md §4 M-2): proves
/// <see cref="DataPipelineOptions.RegisterDefaultNormalizers"/> observably switches the
/// PRODUCTION dispatch path of <see cref="IDataPipeline.ExecuteAsync{TRequest, TRecord}"/> — not
/// merely container contents. No test in this suite registers an
/// <c>INormalizer&lt;Bar, Bar&gt;</c> by hand; the only thing that can make the returned
/// <see cref="Bar"/> normalized is the kernel's own §2.2 registration block reacting to the flag.
/// </summary>
public sealed class NormalizerRegistrationPipelineTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-normreg-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Builds the kernel exactly the way a real host does — through
    /// <see cref="ServiceCollectionExtensions.AddMarketDataKernel"/> — WITHOUT hand-registering
    /// any <c>INormalizer&lt;Bar, Bar&gt;</c>. The only normalizer that can ever reach the pipeline
    /// in this test class is the one the kernel's own flag-gated block registers.
    /// </summary>
    private ServiceProvider BuildKernel(
        IDataSourceAdapter<PriceHistoryRequest, Bar> adapter,
        bool registerDefaultNormalizers)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_tempDir, "snapshots"),
            rawDocumentDirectory: Path.Combine(_tempDir, "raw"),
            checkpointDirectory: Path.Combine(_tempDir, "checkpoints"),
            eventLogDirectory: Path.Combine(_tempDir, "events"),
            configurePipeline: o => o.RegisterDefaultNormalizers = registerDefaultNormalizers);
        services.AddSingleton(adapter);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A deliberately un-normalized <see cref="Bar"/>: negative volume, an over-precision close
    /// price, and inverted High/Low — the exact three defects <c>BarNormalizer</c> fixes (round to
    /// 4 dp, clamp negative volume to zero, swap inverted High/Low).
    /// </summary>
    private static Bar RawBar(DateOnly date) => new(
        Date: date,
        Open: 100.123456m,
        High: 50.000000m,   // inverted — lower than Low
        Low: 60.000000m,
        Close: 101.987654m,
        AdjustedClose: 101.987654m,
        Volume: -500);

    private static IDataSourceAdapter<PriceHistoryRequest, Bar> BuildAdapter(Bar rawBar)
    {
        var provenance = new DataProvenance(
            new ProviderCode("fake-vendor"), "AAPL", LicenseType.Free, RetrievalMode.Api,
            FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, "https://fake.vendor.test/aapl");

        return new FakeDataSourceAdapter<PriceHistoryRequest, Bar>(
            new ProviderCode("fake-vendor"),
            _ => new DataEnvelope<IReadOnlyList<Bar>>(
                [rawBar],
                new DataCoverage(1, 1, 0, 1.0m),
                [],
                [provenance]));
    }

    private static PriceHistoryRequest BuildRequest(DateOnly date) =>
        new([new Symbol("AAPL")], new DateRange(date, date));

    [Fact]
    public async Task FlagOn_NormalizesTheProductionDispatchPath_NoManualNormalizerRegistered()
    {
        var date = new DateOnly(2026, 5, 4);
        var rawBar = RawBar(date);

        using var sp = BuildKernel(BuildAdapter(rawBar), registerDefaultNormalizers: true);
        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = BuildRequest(date);

        var result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request)
            .ConfigureAwait(true);

        result.Payload.Should().HaveCount(1);
        var bar = result.Payload[0];

        // BarNormalizer's three transformations, all observed through the production dispatch
        // path — proves the flag, not a hand-registered normalizer, produced this shape.
        bar.Close.Should().Be(101.9877m); // rounded to 4 dp, AwayFromZero
        bar.Open.Should().Be(100.1235m);
        bar.Volume.Should().Be(0); // negative volume clamped to zero
        bar.High.Should().Be(60m); // swapped: High >= Low afterward
        bar.Low.Should().Be(50m);
    }

    [Fact]
    public async Task FlagOff_ReturnsRawUnnormalizedData_ThroughTheSameProductionDispatchPath()
    {
        var date = new DateOnly(2026, 5, 5);
        var rawBar = RawBar(date);

        using var sp = BuildKernel(BuildAdapter(rawBar), registerDefaultNormalizers: false);
        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = BuildRequest(date);

        var result = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(request)
            .ConfigureAwait(true);

        result.Payload.Should().HaveCount(1);
        var bar = result.Payload[0];

        // Exactly the raw values the fake adapter emitted — un-rounded, un-clamped, un-swapped.
        bar.Close.Should().Be(rawBar.Close);
        bar.Open.Should().Be(rawBar.Open);
        bar.Volume.Should().Be(rawBar.Volume);
        bar.High.Should().Be(rawBar.High);
        bar.Low.Should().Be(rawBar.Low);
    }
}
