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

using System.Runtime.CompilerServices;

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.MarketData.Orchestration.Commands;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

[Trait("Tier", "Integration")]
public sealed class Round3ImportRegressionTests
{
    [Fact]
    public async Task Factor_snapshot_requires_row_projection_before_append()
    {
        var date = new DateOnly(2024, 3, 12);
        await AssertRefusedAsync<FactorSeriesRequest, FactorObservation, FactorObservationDatasetKey>(
            new(new FactorDatasetId("F-F_Research_Data_Factors_daily"), new(date, date)),
            [new(date, "SMB", 0.02m), new(date, "HML", 0.03m)]).ConfigureAwait(true);
    }

    [Fact]
    public async Task Yield_snapshot_requires_row_projection_before_append()
    {
        var date = new DateOnly(2024, 3, 12);
        await AssertRefusedAsync<YieldCurveQuoteRequest, YieldCurveQuote, YieldCurveQuoteDatasetKey>(
            new(new YieldCurveId("USD"), date),
            [new(date, "1Y", 0.02m), new(date, "2Y", 0.03m)]).ConfigureAwait(true);
    }

    [Fact]
    public async Task Futures_snapshot_requires_row_projection_before_append()
    {
        var date = new DateOnly(2024, 3, 12);
        var product = new FuturesProductCode("SR3");
        await AssertRefusedAsync<FuturesSettlementRequest, FuturesSettlement, FuturesSettlementDatasetKey>(
            new(product, new(date, date)),
            [new(date, product, new ContractMonth(2024, 6), 95m, 0.05m),
                new(date, product, new ContractMonth(2024, 9), 96m, 0.04m)]).ConfigureAwait(true);
    }

    [Fact]
    public async Task Option_snapshot_requires_row_projection_before_append()
    {
        var date = new DateOnly(2024, 3, 12);
        await AssertRefusedAsync<OptionChainRequest, OptionQuote, OptionQuoteDatasetKey>(
            new("SPY", new(date, date)),
            [new(date, 1m, 2m, 1.5m, 0.2m, 100, 10), new(date, 3m, 4m, 3.5m, 0.3m, 200, 20)]).ConfigureAwait(true);
    }

    [Fact]
    public async Task Import_fixture_preserves_pre_existing_files_in_shared_temporary_parent()
    {
        var parent = Path.Combine(Path.GetTempPath(), $"import-cleanup-{Guid.NewGuid():N}");
        var sharedRoot = Path.Combine(parent, nameof(Round3ImportRegressionTests), nameof(FactorSeriesRequest));
        Directory.CreateDirectory(sharedRoot);
        var sentinel = Path.Combine(sharedRoot, "pre-existing.txt");
        await File.WriteAllTextAsync(sentinel, "preserve me").ConfigureAwait(true);
        try
        {
            var date = new DateOnly(2024, 3, 12);
            await AssertRefusedAsync<FactorSeriesRequest, FactorObservation, FactorObservationDatasetKey>(
                new(new FactorDatasetId("F-F_Research_Data_Factors_daily"), new(date, date)),
                [new(date, "SMB", 0.02m)], parent).ConfigureAwait(true);

            File.Exists(sentinel).Should().BeTrue("fixture cleanup must preserve pre-existing files");
            (await File.ReadAllTextAsync(sentinel).ConfigureAwait(true)).Should().Be("preserve me");
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    private static async Task AssertRefusedAsync<TRequest, TRecord, TKey>(
        TRequest request, IReadOnlyList<TRecord> records, string? tempDirectory = null)
        where TRequest : IDataRequest
        where TRecord : ISerializable<TRecord>, IHasDate, new()
        where TKey : ISerializable<TKey>, IComparable<TKey>, new()
    {
        var root = Path.Combine(tempDirectory ?? Path.GetTempPath(), nameof(Round3ImportRegressionTests),
            typeof(TRequest).Name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddMarketDataKernel(
                snapshotDirectory: Path.Combine(root, "snapshots"),
                rawDocumentDirectory: Path.Combine(root, "raw"),
                checkpointDirectory: Path.Combine(root, "checkpoints"),
                eventLogDirectory: Path.Combine(root, "events"),
                timeSeriesDirectory: Path.Combine(root, "timeseries"));
            await using var provider = services.BuildServiceProvider();
            var registry = provider.GetRequiredService<ITimeSeriesStoreRegistry>();
            var builder = provider.GetRequiredService<IDatasetKeyBuilder<TRequest, TKey>>();
            var command = new ImportLegacyL2SnapshotsCommand<TRequest, TRecord, TKey>(
                registry, builder, new SnapshotSource<TRequest, TRecord>(request, records));

            await FluentActions.Invoking(() => command.ExecuteAsync()).Should()
                .ThrowAsync<NotSupportedException>().ConfigureAwait(true);

            var stored = new List<TRecord>();
            await foreach (var record in registry.Resolve<TKey, TRecord>()!
                .GetRangeAsync(builder.Build(request), request.Range.From, request.Range.To))
            {
                stored.Add(record);
            }
            stored.Should().BeEmpty("refused snapshots must not append under a placeholder key");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SnapshotSource<TRequest, TRecord>(TRequest request, IReadOnlyList<TRecord> records)
        : ILegacyL2SnapshotSource<TRequest, TRecord>
        where TRequest : IDataRequest
    {
        public async IAsyncEnumerable<LegacyL2Snapshot<TRequest, TRecord>> EnumerateAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(request, records);
            await Task.CompletedTask.ConfigureAwait(true);
        }
    }
}
