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
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Abstractions.Snapshots;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.DependencyInjection;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

public sealed class Round2SnapshotRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"marketdata-r2-snapshot-{Guid.NewGuid():N}");
    private static readonly DateOnly s_day = new(2024, 1, 2);

    [Fact]
    public async Task Factor_snapshot_reads_each_discriminant_and_remains_pinned()
    {
        FactorObservation[] expected = [new(s_day, "SMB", 0.01m), new(s_day, "HML", 0.02m)];
        var source = new Source<FactorSeriesRequest, FactorObservation>(expected);
        using var provider = Host(source);
        var pipeline = provider.GetRequiredService<IDataPipeline>();
        var request = new FactorSeriesRequest(new FactorDatasetId("FACTORS"), new(s_day, s_day));
        await pipeline.ExecuteAsync<FactorSeriesRequest, FactorObservation>(request).ConfigureAwait(true);
        await using var snapshot = await pipeline.AcquireReadSnapshotAsync().ConfigureAwait(true);

        var read = async () => await snapshot.ExecuteAsync<FactorObservation>(request).ConfigureAwait(true);
        var result = await read.Should().NotThrowAsync("both stored factor series cover the requested day");
        result.Subject.Payload.Should().BeEquivalentTo(expected);
        var store = provider.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<FactorObservationDatasetKey, FactorObservation>()!;
        await store.AppendAsync(new("default", "FACTORS", DataFrequency.Daily, "SMB"),
            [new(s_day, "SMB", 0.99m)]).ConfigureAwait(true);
        (await snapshot.ExecuteAsync<FactorObservation>(request).ConfigureAwait(true)).Payload
            .Should().BeEquivalentTo(expected, "a later live append cannot change the captured view");
        source.Calls.Should().Be(1, "snapshot reads never dispatch adapters");
    }

    [Fact]
    public async Task Yield_snapshot_reads_each_tenor_and_rejects_a_known_tenor_gap()
    {
        YieldCurveQuote[] expected = [new(s_day, "1Y", 0.03m), new(s_day, "10Y", 0.04m)];
        var source = new Source<YieldCurveQuoteRequest, YieldCurveQuote>(expected);
        using var provider = Host(source);
        var pipeline = provider.GetRequiredService<IDataPipeline>();
        var request = new YieldCurveQuoteRequest(new YieldCurveId("CURVE"), s_day);
        await pipeline.ExecuteAsync<YieldCurveQuoteRequest, YieldCurveQuote>(request).ConfigureAwait(true);
        await using var snapshot = await pipeline.AcquireReadSnapshotAsync().ConfigureAwait(true);
        var read = async () => await snapshot.ExecuteAsync<YieldCurveQuote>(request).ConfigureAwait(true);
        var result = await read.Should().NotThrowAsync("both stored tenor series cover the requested day");
        result.Subject.Payload.Should().BeEquivalentTo(expected);

        var store = provider.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<YieldCurveQuoteDatasetKey, YieldCurveQuote>()!;
        await store.AppendAsync(new("default", "CURVE", "30Y"),
            [new(s_day.AddDays(-1), "30Y", 0.05m)]).ConfigureAwait(true);
        await using var later = await pipeline.AcquireReadSnapshotAsync().ConfigureAwait(true);
        var gap = async () => await later.ExecuteAsync<YieldCurveQuote>(request).ConfigureAwait(true);
        await gap.Should().ThrowAsync<SnapshotGapException>("every known tenor must cover the requested date");
        (await snapshot.ExecuteAsync<YieldCurveQuote>(request).ConfigureAwait(true)).Payload
            .Should().BeEquivalentTo(expected, "the new incomplete tenor is invisible to the old snapshot");
        source.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(DataFrequency.Daily)]
    [InlineData(DataFrequency.Weekly)]
    [InlineData(DataFrequency.Monthly)]
    public async Task Snapshot_completeness_uses_request_frequency_and_still_refuses_missing_points(DataFrequency frequency)
    {
        var second = frequency switch
        {
            DataFrequency.Weekly => s_day.AddDays(7),
            DataFrequency.Monthly => s_day.AddMonths(1),
            _ => s_day.AddDays(1),
        };
        ScalarObservation[] expected = [new(s_day, 10m, "decimal"), new(second, 20m, "decimal")];
        var source = new Source<EconomicSeriesRequest, ScalarObservation>(expected);
        using var provider = Host(source);
        var pipeline = provider.GetRequiredService<IDataPipeline>();
        var request = new EconomicSeriesRequest(new EconomicSeriesId("SERIES"), new(s_day, second), frequency);
        await pipeline.ExecuteAsync<EconomicSeriesRequest, ScalarObservation>(request).ConfigureAwait(true);
        await using var snapshot = await pipeline.AcquireReadSnapshotAsync().ConfigureAwait(true);
        var read = async () => await snapshot.ExecuteAsync<ScalarObservation>(request).ConfigureAwait(true);
        var result = await read.Should().NotThrowAsync("two observations satisfy this two-period request");
        result.Subject.Payload.Should().BeEquivalentTo(expected);

        var third = frequency switch
        {
            DataFrequency.Weekly => second.AddDays(7),
            DataFrequency.Monthly => second.AddMonths(1),
            _ => second.AddDays(1),
        };
        var gap = async () => await snapshot.ExecuteAsync<ScalarObservation>(
            request with { Range = new(s_day, third) }).ConfigureAwait(true);
        await gap.Should().ThrowAsync<SnapshotGapException>("the third requested period has no observation");
        source.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(LicenseType.Commercial)]
    [InlineData(LicenseType.ResearchOnly)]
    [InlineData(LicenseType.Unknown)]
    [InlineData(LicenseType.Free)]
    public async Task Snapshot_without_source_metadata_never_invents_free_rights(LicenseType license)
    {
        ScalarObservation[] expected = [new(s_day, 3m, "decimal")];
        var source = new Source<EconomicSeriesRequest, ScalarObservation>(expected, license);
        using var provider = Host(source);
        var pipeline = provider.GetRequiredService<IDataPipeline>();
        var request = new EconomicSeriesRequest(new EconomicSeriesId("LICENSE"), new(s_day, s_day));
        var live = await pipeline.ExecuteAsync<EconomicSeriesRequest, ScalarObservation>(request).ConfigureAwait(true);
        live.Provenance.Should().ContainSingle().Which.LicenseFlag.Should().Be(license);
        await using var snapshot = await pipeline.AcquireReadSnapshotAsync().ConfigureAwait(true);
        var result = await snapshot.ExecuteAsync<ScalarObservation>(request).ConfigureAwait(true);
        result.Payload.Should().BeEquivalentTo(expected);
        result.Provenance.Should().ContainSingle().Which.LicenseFlag.Should().Be(LicenseType.Unknown,
            "the snapshot binding has no captured license evidence, even when the live source was Free");
        source.Calls.Should().Be(1);
    }

    private ServiceProvider Host<TRequest, TRecord>(Source<TRequest, TRecord> source)
        where TRequest : IDataRequest
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_directory, "snapshots"),
            rawDocumentDirectory: Path.Combine(_directory, "raw"),
            checkpointDirectory: Path.Combine(_directory, "checkpoints"),
            eventLogDirectory: Path.Combine(_directory, "events"),
            timeSeriesDirectory: Path.Combine(_directory, "timeseries"),
            configurePipeline: options =>
            {
                options.UseTimeSeriesStore = true;
                options.L1CacheTtl = TimeSpan.Zero;
            });
        services.AddSingleton<IDataSourceAdapter<TRequest, TRecord>>(source);
        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class Source<TRequest, TRecord>(IReadOnlyList<TRecord> rows, LicenseType license = LicenseType.Free)
        : IDataSourceAdapter<TRequest, TRecord> where TRequest : IDataRequest
    {
        public ProviderCode ProviderKey { get; } = new("snapshot-source");
        public int Calls { get; private set; }
        public bool CanHandle(TRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<TRecord>>> FetchAsync(
            TRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new DataEnvelope<IReadOnlyList<TRecord>>(
                rows, new(rows.Count, rows.Count, 0, 1m), [],
                [new(ProviderKey, request.DatasetKey, license, RetrievalMode.Api, FreshnessClass.EndOfDay,
                    new DateTimeOffset(2024, 1, 2, 20, 0, 0, TimeSpan.Zero), "https://snapshot.test/source")]));
        }
    }
}
