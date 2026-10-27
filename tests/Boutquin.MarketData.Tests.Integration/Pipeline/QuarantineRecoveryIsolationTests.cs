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
using System.Buffers.Binary;

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.MarketData.Orchestration.Pipeline;

using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries.Columnar;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>Regression coverage for request-owned quarantine recovery.</summary>
public sealed class QuarantineRecoveryIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"marketdata-quarantine-isolation-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Concurrent_clean_request_does_not_dispatch_a_different_requests_quarantined_range()
    {
        var from = new DateOnly(2026, 4, 13);
        var to = from.AddDays(2);
        var damagedAdapter = new SelectivePriceAdapter("damaged-provider", "DAMAGED");
        var cleanAdapter = new SelectivePriceAdapter("clean-provider", "CLEAN");
        using var host = BuildHost(
            "cross-provider-race",
            services =>
            {
                services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(damagedAdapter);
                services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(cleanAdapter);
            });

        var registry = host.GetRequiredService<IMutableTimeSeriesStoreRegistry>();
        var store = await InstallCorruptedDamagedStoreAsync(host, "cross-provider-race", from, to);
        // PriceBarDatasetKeyBuilder's established cache identity uses the configured default
        // provider. Seed both requests under that identity so this test isolates quarantine
        // ownership rather than a provider-key cache miss.
        var cleanKey = new PriceBarDatasetKey("default", DataFrequency.Daily, "CLEAN", null);

        await store.AppendAsync(cleanKey,
        [
            new Bar(from, 201m, 201m, 201m, 201m, 201m, 200),
            new Bar(from.AddDays(1), 202m, 202m, 202m, 202m, 202m, 200),
            new Bar(to, 203m, 203m, 203m, 203m, 203m, 200),
        ]);

        var pipeline = host.GetRequiredService<IDataPipeline>();
        var damagedTask = pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            new PriceHistoryRequest([new Symbol("DAMAGED")], new DateRange(from, to)));
        await store.DamagedReadObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));

        DataEnvelope<IReadOnlyList<Bar>> clean;
        try
        {
            clean = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
                new PriceHistoryRequest([new Symbol("CLEAN")], new DateRange(from, to)));
        }
        finally
        {
            store.ContinueDamagedRequest.TrySetResult(true);
        }

        var damaged = await damagedTask;
        clean.Payload.Should().HaveCount(3);
        damaged.Payload.Should().HaveCount(3).And.OnlyContain(row => row.Close == 900m);
        damagedAdapter.Dispatches.Should().ContainSingle().Which.Should().Be(new DateRange(from, to));
        cleanAdapter.Dispatches.Should().BeEmpty(
            "an unrelated physical dataset must not consume the damaged dataset's recovery range");
    }

    [Fact]
    public async Task Concurrent_overlapping_requests_for_the_same_dataset_each_recover_their_own_range()
    {
        var from = new DateOnly(2026, 4, 13);
        var to = from.AddDays(2);
        var followerFrom = from.AddDays(1);
        var adapter = new SelectivePriceAdapter("damaged-provider", "DAMAGED");
        using var host = BuildHost(
            "same-dataset-race",
            services => services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter));

        var store = await InstallCorruptedDamagedStoreAsync(host, "same-dataset-race", from, to);
        var pipeline = host.GetRequiredService<IDataPipeline>();
        var ownerTask = pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            new PriceHistoryRequest([new Symbol("DAMAGED")], new DateRange(from, to)));
        await store.DamagedReadObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));

        DataEnvelope<IReadOnlyList<Bar>> follower;
        try
        {
            // This is a distinct request/cache key. Its range overlaps the owner's corrupted row
            // group, so both requests must retain their own clipped recovery work.
            follower = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
                new PriceHistoryRequest([new Symbol("DAMAGED")], new DateRange(followerFrom, to)));
        }
        finally
        {
            store.ContinueDamagedRequest.TrySetResult(true);
        }

        var owner = await ownerTask;
        owner.Payload.Should().HaveCount(3).And.OnlyContain(row => row.Close == 900m);
        follower.Payload.Should().HaveCount(2).And.OnlyContain(row => row.Close == 900m);
        adapter.Dispatches.Should().BeEquivalentTo(
            [new DateRange(from, to), new DateRange(followerFrom, to)]);
    }

    [Fact]
    public async Task Cancelled_quarantine_owner_releases_its_claim_so_a_bounded_retry_can_recover()
    {
        var from = new DateOnly(2026, 4, 13);
        var to = from.AddDays(2);
        var retryFrom = from.AddDays(1);
        var adapter = new SelectivePriceAdapter("damaged-provider", "DAMAGED");
        using var host = BuildHost(
            "cancelled-owner-retry",
            services => services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(adapter));

        var store = await InstallCorruptedDamagedStoreAsync(host, "cancelled-owner-retry", from, to);
        var pipeline = host.GetRequiredService<IDataPipeline>();
        using var cancellation = new CancellationTokenSource();
        var cancelledTask = pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            new PriceHistoryRequest([new Symbol("DAMAGED")], new DateRange(from, to)),
            cancellation.Token);
        await store.DamagedReadObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await FluentActions.Awaiting(() => cancelledTask)
            .Should().ThrowAsync<OperationCanceledException>();

        var retry = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(
            new PriceHistoryRequest([new Symbol("DAMAGED")], new DateRange(retryFrom, to)));

        retry.Payload.Should().HaveCount(2).And.OnlyContain(row => row.Close == 900m);
        adapter.Dispatches.Should().ContainSingle().Which.Should().Be(new DateRange(retryFrom, to));
    }

    private async Task<PauseAfterDamagedReadStore> InstallCorruptedDamagedStoreAsync(
        ServiceProvider host,
        string slot,
        DateOnly from,
        DateOnly to)
    {
        var registry = host.GetRequiredService<IMutableTimeSeriesStoreRegistry>();
        var inner = registry.Resolve<PriceBarDatasetKey, Bar>()!;
        var store = new PauseAfterDamagedReadStore(inner, "DAMAGED");
        registry.Register<PriceBarDatasetKey, Bar>(store);
        var damagedKey = new PriceBarDatasetKey("default", DataFrequency.Daily, "DAMAGED", null);
        await store.AppendAsync(damagedKey,
        [
            new Bar(from, 101m, 101m, 101m, 101m, 101m, 100),
            new Bar(from.AddDays(1), 102m, 102m, 102m, 102m, 102m, 100),
            new Bar(to, 103m, 103m, 103m, 103m, 103m, 100),
        ]);
        await registry.ResolveEngine<PriceBarDatasetKey, Bar>().FlushAsync();
        await CorruptOnlyColumnarSegmentAsync(slot).ConfigureAwait(true);
        return store;
    }

    private ServiceProvider BuildHost(string slot, Action<IServiceCollection> configure)
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
        configure(services);
        return services.BuildServiceProvider();
    }

    private async Task CorruptOnlyColumnarSegmentAsync(string slot)
    {
        var path = Directory.GetFiles(Path.Combine(_root, slot, "timeseries"), "*.bsct", SearchOption.AllDirectories)
            .Should().ContainSingle().Subject;
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
        var outerFooterLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(bytes.Length - 8, 4)));
        var innerLength = bytes.Length - 8 - outerFooterLength - 8;
        var inner = bytes.AsSpan(8, innerLength);
        var innerFooterLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(inner[^8..^4]));
        var innerFooterStart = inner.Length - 8 - 4 - innerFooterLength;
        var innerFooter = inner.Slice(innerFooterStart, innerFooterLength);
        ReadOnlySpan<byte> oneRowGroupCrcHeader = [0x03, 0x06, 0x00, 0x00, 0x00, 0x01, 0x00];
        var extensionOffset = innerFooter.IndexOf(oneRowGroupCrcHeader);
        extensionOffset.Should().BeGreaterThanOrEqualTo(0);
        innerFooter[extensionOffset + oneRowGroupCrcHeader.Length] ^= 0xff;
        BinaryPrimitives.WriteUInt32LittleEndian(
            inner.Slice(innerFooterStart + innerFooterLength, 4), ComputeCrc32C(innerFooter));
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(true);
    }

    private static uint ComputeCrc32C(ReadOnlySpan<byte> bytes)
    {
        const uint reflectedCastagnoliPolynomial = 0x82f63b78u;
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : reflectedCastagnoliPolynomial);
            }
        }

        return ~crc;
    }

    private sealed class SelectivePriceAdapter(string provider, string ticker)
        : IDataSourceAdapter<PriceHistoryRequest, Bar>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new(provider);
        public int Priority => 100;
        public List<DateRange> Dispatches { get; } = [];
        public bool CanHandle(PriceHistoryRequest request)
            => request.Symbols.Count == 1 && request.Symbols[0].Ticker == ticker;

        public Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(
            PriceHistoryRequest request,
            CancellationToken cancellationToken = default)
        {
            Dispatches.Add(request.Range);
            var rows = Enumerable.Range(0, request.Range.To.DayNumber - request.Range.From.DayNumber + 1)
                .Select(offset => new Bar(
                    request.Range.From.AddDays(offset), 900m, 900m, 900m, 900m, 900m, 900))
                .ToList();
            return Task.FromResult(new DataEnvelope<IReadOnlyList<Bar>>(
                rows,
                new DataCoverage(rows.Count, rows.Count, 0, 1m),
                [],
                [new DataProvenance(
                    ProviderKey, request.DatasetKey, LicenseType.Free, RetrievalMode.Api,
                    FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, $"https://{provider}.test/{request.DatasetKey}")]));
        }
    }

    private sealed class PauseAfterDamagedReadStore(
        ITimeSeriesStore<PriceBarDatasetKey, Bar> inner,
        string damagedTicker)
        : ITimeSeriesStore<PriceBarDatasetKey, Bar>, IQuarantineRecoverySource
    {
        private int _paused;

        public TaskCompletionSource<bool> DamagedReadObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ContinueDamagedRequest { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task AppendAsync(
            PriceBarDatasetKey dataset,
            IEnumerable<Bar> records,
            CancellationToken cancellationToken = default)
            => inner.AppendAsync(dataset, records, cancellationToken);

        public async IAsyncEnumerable<Bar> GetRangeAsync(
            PriceBarDatasetKey dataset,
            DateOnly from,
            DateOnly to,
            ColumnProjection? projection = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var row in inner.GetRangeAsync(dataset, from, to, projection, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return row;
            }

            if (dataset.Ticker == damagedTicker && Interlocked.Exchange(ref _paused, 1) == 0)
            {
                DamagedReadObserved.TrySetResult(true);
                await ContinueDamagedRequest.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public Task<DateRange?> GetCoverageAsync(
            PriceBarDatasetKey dataset,
            CancellationToken cancellationToken = default)
            => inner.GetCoverageAsync(dataset, cancellationToken);

        public Task<IReadOnlyList<DateRange>> GetGapsAsync(
            PriceBarDatasetKey dataset,
            DateRange requested,
            CancellationToken cancellationToken = default)
            => inner.GetGapsAsync(dataset, requested, cancellationToken);

        public IAsyncEnumerable<Bar> GetTailAsync(
            PriceBarDatasetKey dataset,
            int count,
            DateOnly? onOrBefore = null,
            ColumnProjection? projection = null,
            CancellationToken cancellationToken = default)
            => inner.GetTailAsync(dataset, count, onOrBefore, projection, cancellationToken);

        public Task DeleteRangeAsync(
            PriceBarDatasetKey dataset,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default)
            => inner.DeleteRangeAsync(dataset, from, to, cancellationToken);

        public IReadOnlyList<DateRange> DrainQuarantinedRanges()
            => ((IQuarantineRecoverySource)inner).DrainQuarantinedRanges();
    }
}
