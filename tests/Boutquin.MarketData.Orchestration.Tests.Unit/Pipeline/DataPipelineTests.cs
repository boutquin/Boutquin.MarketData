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
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.Memory;
using Boutquin.MarketData.Cache.Policies;
using Boutquin.MarketData.Cache.WriteThrough;
using Boutquin.MarketData.Calendars;
using Boutquin.MarketData.Orchestration.Coverage;
using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.MarketData.Storage.Checkpointing;
using Boutquin.MarketData.Storage.Events;
using Boutquin.MarketData.Storage.InMemory;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.Storage.Domain.Interfaces.Streaming;
using Boutquin.Storage.Domain.ValueObjects;
using Boutquin.Storage.Infrastructure.Events;
using Boutquin.Storage.Infrastructure.Streaming.Checkpointing;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Boutquin.MarketData.Orchestration.Tests.Unit.Pipeline;

#region Test doubles

internal sealed record TestRecord(string Symbol, decimal Value);

internal sealed record TestRequest(
    string DatasetKey,
    DateRange Range,
    DataFrequency Frequency = DataFrequency.Daily) : IDataRequest;

internal sealed record BatchTestRequest(
    IReadOnlyList<string> Symbols,
    DateRange Range,
    DataFrequency Frequency = DataFrequency.Daily) : IDataRequest, IBatchDataRequest<string>
{
    // Join with '_' (not '+') to match PriceHistoryRequest's real convention: '+' is rejected by
    // FileSystemObjectStore's key validator and by Storage 2.0's checkpoint ValidateProcessorId
    // ([alphanumeric, '.', '_', '-']); '_' is the production join character.
    public string DatasetKey => string.Join("_", Symbols);
    public IReadOnlyList<string> PartitionKeys => Symbols;
}

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
}

internal sealed class FakeAdapter : IDataSourceAdapter<TestRequest, TestRecord>, IPrioritizedAdapter
{
    private readonly Func<TestRequest, CancellationToken, Task<DataEnvelope<IReadOnlyList<TestRecord>>>> _fetch;

    public FakeAdapter(
        string providerKey,
        int priority,
        Func<TestRequest, CancellationToken, Task<DataEnvelope<IReadOnlyList<TestRecord>>>> fetch)
    {
        ProviderKey = new ProviderCode(providerKey);
        Priority = priority;
        _fetch = fetch;
    }

    public ProviderCode ProviderKey { get; }
    public int Priority { get; }
    public bool CanHandle(TestRequest request) => true;

    public Task<DataEnvelope<IReadOnlyList<TestRecord>>> FetchAsync(
        TestRequest request, CancellationToken cancellationToken = default) =>
        _fetch(request, cancellationToken);
}

internal sealed class FakeBatchAdapter : IDataSourceAdapter<BatchTestRequest, TestRecord>, IPrioritizedAdapter
{
    private readonly Func<BatchTestRequest, CancellationToken, Task<DataEnvelope<IReadOnlyList<TestRecord>>>> _fetch;

    public FakeBatchAdapter(
        string providerKey,
        int priority,
        Func<BatchTestRequest, CancellationToken, Task<DataEnvelope<IReadOnlyList<TestRecord>>>> fetch)
    {
        ProviderKey = new ProviderCode(providerKey);
        Priority = priority;
        _fetch = fetch;
    }

    public ProviderCode ProviderKey { get; }
    public int Priority { get; }
    public bool CanHandle(BatchTestRequest request) => true;

    public Task<DataEnvelope<IReadOnlyList<TestRecord>>> FetchAsync(
        BatchTestRequest request, CancellationToken cancellationToken = default) =>
        _fetch(request, cancellationToken);
}

internal sealed class TestRecordSerializer : ISnapshotSerializer<SnapshotEnvelope<IReadOnlyList<TestRecord>>>
{
    private readonly SnapshotEnvelopeSerializer<IReadOnlyList<TestRecord>> _inner;

    public TestRecordSerializer()
    {
        _inner = new SnapshotEnvelopeSerializer<IReadOnlyList<TestRecord>>(new TestRecordListSerializer());
    }

    public Task SerializeAsync(Stream stream, SnapshotEnvelope<IReadOnlyList<TestRecord>> data,
        CancellationToken cancellationToken = default) =>
        _inner.SerializeAsync(stream, data, cancellationToken);

    public Task<SnapshotEnvelope<IReadOnlyList<TestRecord>>> DeserializeAsync(Stream stream,
        CancellationToken cancellationToken = default) =>
        _inner.DeserializeAsync(stream, cancellationToken);
}

internal sealed class TestRecordListSerializer : ISnapshotSerializer<IReadOnlyList<TestRecord>>
{
    public async Task SerializeAsync(Stream stream, IReadOnlyList<TestRecord> data,
        CancellationToken cancellationToken = default)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data);
        await stream.WriteAsync(json, cancellationToken).ConfigureAwait(true);
    }

    public async Task<IReadOnlyList<TestRecord>> DeserializeAsync(Stream stream,
        CancellationToken cancellationToken = default)
    {
        return (await System.Text.Json.JsonSerializer.DeserializeAsync<List<TestRecord>>(
            stream, cancellationToken: cancellationToken).ConfigureAwait(true))!;
    }
}

internal sealed class UpperCaseNormalizer : INormalizer<TestRecord, TestRecord>
{
    public TestRecord Normalize(TestRecord raw) => raw with { Symbol = raw.Symbol.ToUpperInvariant() };
}

internal sealed class TestPartitioner : IRequestPartitioner<TestRequest>
{
    private readonly IReadOnlyList<TestRequest> _partitions;
    public TestPartitioner(IReadOnlyList<TestRequest> partitions) => _partitions = partitions;
    public IReadOnlyList<TestRequest> Partition(TestRequest request) => _partitions;
}

internal sealed class RecordingCheckpointStore : ICheckpointStore
{
    private readonly Dictionary<string, (long Offset, byte[] State)> _checkpoints = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> ProcessorIds => _checkpoints.Keys;

    public ValueTask SaveAsync(
        string processorId,
        long offset,
        byte[] state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _checkpoints[processorId] = (offset, state.ToArray());
        return ValueTask.CompletedTask;
    }

    public ValueTask<(long Offset, byte[] State)?> LoadAsync(
        string processorId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _checkpoints.TryGetValue(processorId, out var checkpoint)
            ? ValueTask.FromResult<(long Offset, byte[] State)?>(
                (checkpoint.Offset, checkpoint.State.ToArray()))
            : ValueTask.FromResult<(long Offset, byte[] State)?>(null);
    }
}

#endregion

public sealed class DataPipelineTests
{
    private static readonly DateRange s_range = new(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5));
    private static readonly DataProvenance s_provenance = new(new ProviderCode("test-provider"), "AAPL", LicenseType.Free, RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, null);

    private static DataEnvelope<IReadOnlyList<TestRecord>> MakeEnvelope(params TestRecord[] records) =>
        new(records.ToList(),
            new DataCoverage(records.Length, records.Length, 0, records.Length > 0 ? 1.0m : 0.0m),
            [],
            [s_provenance]);

    private static (DataPipeline Pipeline, IServiceProvider Services, FakeClock Clock, ExactOnceMemoryCache L1, InMemorySnapshotStore SnapshotStore, IngestionEventLog EventLog) BuildPipeline(
        Action<IServiceCollection>? configureServices = null,
        DataPipelineOptions? options = null)
    {
        var clock = new FakeClock();
        var l1 = new ExactOnceMemoryCache();
        var snapshotStore = new InMemorySnapshotStore();
        var checkpointStore = new IngestionCheckpointStore(new InMemoryCheckpointStore());
        var eventLogInner = new InMemoryEventLog<IngestionEvent>();
        var eventLog = new IngestionEventLog(eventLogInner);
        var stalenessPolicy = new SnapshotStalenessPolicy(TimeSpan.FromHours(24));
        var coverageCalc = new CoverageCalculator(new WeekendOnlyCalendar());
        var opts = Options.Create(options ?? new DataPipelineOptions());

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        configureServices?.Invoke(services);
        var sp = services.BuildServiceProvider();

        var pipeline = new DataPipeline(
            l1, snapshotStore, checkpointStore, eventLog,
            stalenessPolicy, coverageCalc, opts, sp, clock,
            NullLogger<DataPipeline>.Instance);

        return (pipeline, sp, clock, l1, snapshotStore, eventLog);
    }

    private static (DataPipeline Pipeline, WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<TestRecord>>>? WriteThrough) BuildBatchPipeline(
        IngestionCheckpointStore checkpointStore,
        Func<BatchTestRequest, CancellationToken, Task<DataEnvelope<IReadOnlyList<TestRecord>>>> fetch,
        DataPipelineOptions? options = null,
        InMemorySnapshotStore? snapshotStore = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDataSourceAdapter<BatchTestRequest, TestRecord>>(
            new FakeBatchAdapter("test", 100, fetch));
        services.AddSingleton<IRequestPartitioner<BatchTestRequest>>(new BatchPartitioner());

        WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<TestRecord>>>? writeThrough = null;
        if (snapshotStore is not null)
        {
            writeThrough = new WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<TestRecord>>>(
                snapshotStore,
                new TestRecordSerializer(),
                key => key);
            services.AddSingleton(writeThrough);
        }

        var pipeline = new DataPipeline(
            new ExactOnceMemoryCache(),
            snapshotStore ?? new InMemorySnapshotStore(),
            checkpointStore,
            new IngestionEventLog(new InMemoryEventLog<IngestionEvent>()),
            new SnapshotStalenessPolicy(TimeSpan.FromHours(24)),
            new CoverageCalculator(new WeekendOnlyCalendar()),
            Options.Create(options ?? new DataPipelineOptions()),
            services.BuildServiceProvider(),
            new FakeClock(),
            NullLogger<DataPipeline>.Instance);

        return (pipeline, writeThrough);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cache_identity_preserves_frequency(bool useL1)
    {
        var calls = 0;
        var snapshots = new InMemorySnapshotStore();
        var (pipeline, _, clock, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (request, _) =>
                {
                    calls++;
                    return Task.FromResult(MakeEnvelope(new TestRecord("AAPL", (int)request.Frequency)));
                }));
            svc.AddSingleton(new WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<TestRecord>>>(
                snapshots, new TestRecordSerializer(), key => key));
        }, new DataPipelineOptions { L1CacheTtl = useL1 ? TimeSpan.FromHours(1) : TimeSpan.Zero });
        clock.UtcNow = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await pipeline.ExecuteAsync<TestRequest, TestRecord>(new TestRequest("AAPL", s_range, DataFrequency.Daily)).ConfigureAwait(true);
        var weekly = await pipeline.ExecuteAsync<TestRequest, TestRecord>(new TestRequest("AAPL", s_range, DataFrequency.Weekly)).ConfigureAwait(true);
        weekly.Payload.Single().Value.Should().Be((int)DataFrequency.Weekly);
        calls.Should().Be(2);
        var daily = await pipeline.ExecuteAsync<TestRequest, TestRecord>(new TestRequest("AAPL", s_range, DataFrequency.Daily)).ConfigureAwait(true);
        daily.Payload.Single().Value.Should().Be((int)DataFrequency.Daily);
        calls.Should().Be(2);
    }

    // Test 1
    [Fact]
    public async Task L1_cache_hit_returns_without_calling_adapters()
    {
        var adapterCalled = false;
        var (pipeline, _, _, l1, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (_, _) =>
                {
                    adapterCalled = true;
                    return Task.FromResult(MakeEnvelope(new TestRecord("AAPL", 100m)));
                }));
        });

        var request = new TestRequest("AAPL", s_range);
        var expected = MakeEnvelope(new TestRecord("AAPL", 150m));

        // Pre-populate L1
        var cacheKey = Cache.Keys.CacheKeyBuilder.FromDateRange("pipeline-v2/TestRecord/Daily", "AAPL", s_range.From, s_range.To);
        await l1.SetAsync(cacheKey, expected, TimeSpan.FromHours(1)).ConfigureAwait(true);

        // Act
        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert
        result.Payload.Should().HaveCount(1);
        result.Payload[0].Value.Should().Be(150m);
        adapterCalled.Should().BeFalse();
    }

    // Test 2
    [Fact]
    public async Task L2_snapshot_hit_fresh_returns_without_adapters()
    {
        var adapterCalled = false;
        var sharedSnapshotStore = new InMemorySnapshotStore();

        var (pipeline, _, clock, l1, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (_, _) =>
                {
                    adapterCalled = true;
                    return Task.FromResult(MakeEnvelope(new TestRecord("AAPL", 100m)));
                }));

            svc.AddSingleton(
                new WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<TestRecord>>>(
                    sharedSnapshotStore,
                    new TestRecordSerializer(),
                    key => key));
        });

        // Pre-populate L2 with a fresh snapshot
        var cacheKey = Cache.Keys.CacheKeyBuilder.FromDateRange("pipeline-v2/TestRecord/Daily", "AAPL", s_range.From, s_range.To);
        var metadata = new SnapshotMetadata(DateTimeOffset.UtcNow, [s_provenance], []);
        var snapshotEnvelope = new SnapshotEnvelope<IReadOnlyList<TestRecord>>(
            new SchemaVersion(1), metadata, new List<TestRecord> { new("AAPL", 200m) });

        var serializer = new TestRecordSerializer();
        using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, snapshotEnvelope).ConfigureAwait(true);
        stream.Position = 0;
        await sharedSnapshotStore.WriteAsync(cacheKey, stream).ConfigureAwait(true);

        // Act
        var request = new TestRequest("AAPL", s_range);
        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert
        adapterCalled.Should().BeFalse();
        result.Payload.Should().HaveCount(1);
        result.Payload[0].Value.Should().Be(200m);

        // L1 should now be populated
        var l1Result = await l1.GetAsync<DataEnvelope<IReadOnlyList<TestRecord>>>(cacheKey).ConfigureAwait(true);
        l1Result.Should().NotBeNull();
    }

    // Test 3
    [Fact]
    public async Task L2_snapshot_hit_stale_fetches_from_adapter()
    {
        var adapterCalled = false;
        var sharedSnapshotStore = new InMemorySnapshotStore();

        var (pipeline, _, clock, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (_, _) =>
                {
                    adapterCalled = true;
                    return Task.FromResult(MakeEnvelope(new TestRecord("AAPL", 300m)));
                }));

            svc.AddSingleton(
                new WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<TestRecord>>>(
                    sharedSnapshotStore,
                    new TestRecordSerializer(),
                    key => key));
        });

        // Pre-populate L2 with a STALE snapshot (48 hours old)
        var cacheKey = Cache.Keys.CacheKeyBuilder.FromDateRange("pipeline-v2/TestRecord/Daily", "AAPL", s_range.From, s_range.To);
        var staleTime = DateTimeOffset.UtcNow.AddHours(-48);
        var metadata = new SnapshotMetadata(staleTime, [s_provenance], []);
        var snapshotEnvelope = new SnapshotEnvelope<IReadOnlyList<TestRecord>>(
            new SchemaVersion(1), metadata, new List<TestRecord> { new("AAPL", 200m) });

        var serializer = new TestRecordSerializer();
        using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, snapshotEnvelope).ConfigureAwait(true);
        stream.Position = 0;
        await sharedSnapshotStore.WriteAsync(cacheKey, stream).ConfigureAwait(true);

        // Act
        var request = new TestRequest("AAPL", s_range);
        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert
        adapterCalled.Should().BeTrue();
        result.Payload.Should().HaveCount(1);
        result.Payload[0].Value.Should().Be(300m);
    }

    // Test 4
    [Fact]
    public async Task Single_adapter_success_full_flow()
    {
        var (pipeline, _, clock, l1, _, eventLog) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test-provider", 100, (_, _) =>
                    Task.FromResult(MakeEnvelope(
                        new TestRecord("AAPL", 150m),
                        new TestRecord("AAPL", 151m)))));
        });

        // Act
        var request = new TestRequest("AAPL", s_range);
        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert
        result.Payload.Should().HaveCount(2);
        result.Provenance.Should().NotBeEmpty();
        result.Coverage.ReturnedPoints.Should().Be(2);

        // L1 should be populated
        var cacheKey = Cache.Keys.CacheKeyBuilder.FromDateRange("pipeline-v2/TestRecord/Daily", "AAPL", s_range.From, s_range.To);
        var cached = await l1.GetAsync<DataEnvelope<IReadOnlyList<TestRecord>>>(cacheKey).ConfigureAwait(true);
        cached.Should().NotBeNull();

        // Event log should have Fetched event
        var events = new List<IngestionEvent>();
        await foreach (var (_, evt) in eventLog.ReadFromAsync(0).ConfigureAwait(true))
        {
            events.Add(evt);
        }

        events.Should().Contain(e => e.Action == IngestionAction.Fetched);
    }

    // Test 5
    [Fact]
    public async Task Multi_adapter_fallback()
    {
        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("failing-provider", 100, (_, _) =>
                    throw new InvalidOperationException("Source down")));

            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("backup-provider", 50, (_, _) =>
                    Task.FromResult(MakeEnvelope(new TestRecord("AAPL", 99m)))));
        });

        // Act
        var request = new TestRequest("AAPL", s_range);
        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert
        result.Payload.Should().HaveCount(1);
        result.Provenance.Should().Contain(p => p.Provider == new ProviderCode("test-provider"));
    }

    // Test 6
    [Fact]
    public async Task Batch_request_checkpoint_saves_per_symbol()
    {
        var fetchCount = 0;
        var checkpointStoreInner = new RecordingCheckpointStore();
        var checkpointStore = new IngestionCheckpointStore(checkpointStoreInner);

        var clock = new FakeClock();
        var l1 = new ExactOnceMemoryCache();
        var snapshotStore = new InMemorySnapshotStore();
        var eventLogInner = new InMemoryEventLog<IngestionEvent>();
        var eventLog = new IngestionEventLog(eventLogInner);

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDataSourceAdapter<BatchTestRequest, TestRecord>>(
            new FakeBatchAdapter("test", 100, (req, _) =>
            {
                fetchCount++;
                return Task.FromResult(MakeEnvelope(new TestRecord(req.DatasetKey, 1m)));
            }));

        // Register a partitioner that splits into individual symbol requests
        services.AddSingleton<IRequestPartitioner<BatchTestRequest>>(
            new BatchPartitioner());

        var sp = services.BuildServiceProvider();

        var pipeline = new DataPipeline(
            l1, snapshotStore, checkpointStore, eventLog,
            new SnapshotStalenessPolicy(), new CoverageCalculator(new WeekendOnlyCalendar()),
            Options.Create(new DataPipelineOptions()), sp, clock,
            NullLogger<DataPipeline>.Instance);

        // Act
        var request = new BatchTestRequest(["AAPL", "MSFT", "GOOG"], s_range);
        var result = await pipeline.ExecuteAsync<BatchTestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert
        fetchCount.Should().Be(3);

        // Checkpoint should exist
        var pipelineId = checkpointStoreInner.ProcessorIds.Single();
        var cp = await checkpointStore.LoadAsync(pipelineId).ConfigureAwait(true);
        cp.Should().NotBeNull();
        cp!.SymbolIndex.Should().Be(2); // last index
    }

    // Test 7
    [Fact]
    public async Task Batch_request_with_checkpoint_safely_restarts_from_zero()
    {
        var checkpointStoreInner = new RecordingCheckpointStore();
        var checkpointStore = new IngestionCheckpointStore(checkpointStoreInner);
        var request = new BatchTestRequest(["AAPL", "MSFT", "GOOG"], s_range);
        var (firstPipeline, _) = BuildBatchPipeline(
            checkpointStore,
            (partition, _) => Task.FromResult(
                MakeEnvelope(new TestRecord(partition.DatasetKey, 1m))));
        await firstPipeline.ExecuteAsync<BatchTestRequest, TestRecord>(request).ConfigureAwait(true);

        var restartedFetches = new List<string>();
        var (restartedPipeline, _) = BuildBatchPipeline(
            checkpointStore,
            (partition, _) =>
            {
                restartedFetches.Add(partition.DatasetKey);
                return Task.FromResult(MakeEnvelope(new TestRecord(partition.DatasetKey, 1m)));
            });

        var result = await restartedPipeline
            .ExecuteAsync<BatchTestRequest, TestRecord>(request)
            .ConfigureAwait(true);

        restartedFetches.Should().Equal("AAPL", "MSFT", "GOOG");
        result.Payload.Select(record => record.Symbol).Should().Equal("AAPL", "MSFT", "GOOG");
    }

    [Fact]
    public async Task Batch_request_restart_after_cancellation_rebuilds_complete_envelope_and_snapshot()
    {
        var checkpointStoreInner = new RecordingCheckpointStore();
        var checkpointStore = new IngestionCheckpointStore(checkpointStoreInner);
        using var cancellation = new CancellationTokenSource();

        var (interruptedPipeline, _) = BuildBatchPipeline(
            checkpointStore,
            (request, cancellationToken) =>
            {
                if (request.DatasetKey == "MSFT")
                {
                    cancellation.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                return Task.FromResult(MakeEnvelope(new TestRecord(request.DatasetKey, 1m)));
            });

        var request = new BatchTestRequest(["AAPL", "MSFT", "GOOG"], s_range);
        await FluentActions
            .Invoking(() => interruptedPipeline.ExecuteAsync<BatchTestRequest, TestRecord>(
                request,
                cancellation.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>()
            .ConfigureAwait(true);

        checkpointStoreInner.ProcessorIds.Should().ContainSingle(
            "the first successful partition must be checkpointed before cancellation");

        var restartedFetches = new List<string>();
        var snapshotStore = new InMemorySnapshotStore();
        var (restartedPipeline, writeThrough) = BuildBatchPipeline(
            checkpointStore,
            (partition, _) =>
            {
                restartedFetches.Add(partition.DatasetKey);
                return Task.FromResult(MakeEnvelope(new TestRecord(partition.DatasetKey, 1m)));
            },
            snapshotStore: snapshotStore);

        var result = await restartedPipeline
            .ExecuteAsync<BatchTestRequest, TestRecord>(request)
            .ConfigureAwait(true);

        restartedFetches.Should().Equal("AAPL", "MSFT", "GOOG");
        result.Payload.Select(record => record.Symbol).Should().Equal("AAPL", "MSFT", "GOOG");
        result.Provenance.Should().HaveCount(3);
        result.Issues.Should().BeEmpty();
        result.Coverage.ReturnedPoints.Should().Be(3);
        result.Coverage.CoverageRatio.Should().Be(1.0m);

        var cacheKey = Cache.Keys.CacheKeyBuilder.FromDateRange(
            "pipeline-v2/TestRecord/Daily",
            request.DatasetKey,
            request.Range.From,
            request.Range.To);
        var persisted = await writeThrough!.TryReadAsync(cacheKey).ConfigureAwait(true);
        persisted.Should().NotBeNull();
        persisted!.Payload.Select(record => record.Symbol).Should().Equal("AAPL", "MSFT", "GOOG");
        persisted.Metadata.Provenance.Should().HaveCount(3);
        persisted.Metadata.Issues.Should().BeEmpty();
        persisted.Metadata.Coverage!.ReturnedPoints.Should().Be(3);
    }

    [Fact]
    public async Task Batch_checkpoint_advances_only_through_contiguous_successes()
    {
        var checkpointStoreInner = new RecordingCheckpointStore();
        var checkpointStore = new IngestionCheckpointStore(checkpointStoreInner);
        var request = new BatchTestRequest(["AAPL", "MSFT", "GOOG"], s_range);
        var (pipeline, _) = BuildBatchPipeline(
            checkpointStore,
            (partition, _) => partition.DatasetKey == "MSFT"
                ? throw new InvalidOperationException("simulated partition failure")
                : Task.FromResult(MakeEnvelope(new TestRecord(partition.DatasetKey, 1m))),
            new DataPipelineOptions { ContinueOnPartitionFailure = true });

        var result = await pipeline
            .ExecuteAsync<BatchTestRequest, TestRecord>(request)
            .ConfigureAwait(true);

        result.Payload.Select(record => record.Symbol).Should().Equal("AAPL", "GOOG");
        checkpointStoreInner.ProcessorIds.Should().ContainSingle();
        var processorId = checkpointStoreInner.ProcessorIds.Single();
        var checkpoint = await checkpointStore.LoadAsync(processorId).ConfigureAwait(true);
        checkpoint!.SymbolIndex.Should().Be(0,
            "a success after the failed index is not a contiguous committed prefix");
    }

    [Fact]
    public async Task Checkpoint_identity_is_collision_resistant_and_range_specific()
    {
        var checkpointStoreInner = new RecordingCheckpointStore();
        var checkpointStore = new IngestionCheckpointStore(checkpointStoreInner);
        var requests = new[]
        {
            new BatchTestRequest(["A//B"], s_range),
            new BatchTestRequest(["A__B"], s_range),
            new BatchTestRequest(
                ["A//B"],
                new DateRange(s_range.From.AddDays(1), s_range.To.AddDays(1))),
        };

        foreach (var request in requests)
        {
            var (pipeline, _) = BuildBatchPipeline(
                checkpointStore,
                (partition, _) => Task.FromResult(
                    MakeEnvelope(new TestRecord(partition.DatasetKey, 1m))));
            await pipeline.ExecuteAsync<BatchTestRequest, TestRecord>(request).ConfigureAwait(true);
        }

        checkpointStoreInner.ProcessorIds.Should().HaveCount(3,
            "hostile logical keys and distinct ranges must not consume one another's checkpoint");
        checkpointStoreInner.ProcessorIds.Should().OnlyContain(
            id => id.All(c => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '_' || c == '-'));
    }

    [Fact]
    public async Task Checkpoint_identity_uses_named_bounded_stack_allocation_policy()
    {
        var source = ReadDataPipelineSource();
        source.Should().Contain("MaxStackAllocatedCheckpointIdentityBytes");
        source.Should().Contain("utf8ByteCount <= MaxStackAllocatedCheckpointIdentityBytes");
        source.Should().Contain(
            "stackalloc byte[MaxStackAllocatedCheckpointIdentityBytes]",
            "only the named bounded branch may allocate the identity buffer on the stack");
        source.Should().Contain(
            "HashCheckpointIdentity(Encoding.UTF8.GetBytes(canonicalIdentity))",
            "above-threshold identities must use the heap-backed encoder overload");
        source.Should().NotContain("raw.Length <= 256 ? stackalloc");

        var checkpointStoreInner = new RecordingCheckpointStore();
        var checkpointStore = new IngestionCheckpointStore(checkpointStoreInner);
        var largeLogicalKey = new string('X', 16_384);
        var request = new BatchTestRequest([largeLogicalKey], s_range);
        var (pipeline, _) = BuildBatchPipeline(
            checkpointStore,
            (partition, _) => Task.FromResult(
                MakeEnvelope(new TestRecord(partition.DatasetKey, 1m))));

        await pipeline.ExecuteAsync<BatchTestRequest, TestRecord>(request).ConfigureAwait(true);

        checkpointStoreInner.ProcessorIds.Should().ContainSingle();
        checkpointStoreInner.ProcessorIds.Single().Length.Should().BeLessThan(128,
            "large logical identities must be digested after using the heap-backed encoding path");
    }

    // Test 8
    [Fact]
    public async Task All_adapters_fail_returns_partial_envelope()
    {
        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("bad1", 100, (_, _) =>
                    throw new InvalidOperationException("fail1")));
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("bad2", 50, (_, _) =>
                    throw new InvalidOperationException("fail2")));
        });

        // Act
        var request = new TestRequest("AAPL", s_range);
        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert
        result.Payload.Should().BeEmpty();
        result.Issues.Should().Contain(i => i.Code == IssueCode.PartitionFailed);
    }

    private static string ReadDataPipelineSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "Boutquin.MarketData.Orchestration",
                "Pipeline",
                "DataPipeline.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate DataPipeline.cs from the test output path.");
    }

    [Fact]
    public async Task Public_partition_failure_does_not_disclose_adapter_exception_details()
    {
        const string sentinel = "credential file /Users/pierre/private/api-key.txt failed";
        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("private-provider", 100, (_, _) =>
                    throw new InvalidOperationException(sentinel)));
        });

        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(
            new TestRequest("AAPL", s_range)).ConfigureAwait(true);

        result.Issues.Should().ContainSingle(issue => issue.Code == IssueCode.PartitionFailed);
        result.Issues.Should().NotContain(issue => issue.Message.Contains(sentinel, StringComparison.Ordinal));
        result.Issues.Should().OnlyContain(issue =>
            issue.Message == "Partition fetch failed; see provider diagnostics for details.");
    }

    [Fact]
    public async Task Public_recoverable_provider_failure_returns_an_issue_bearing_missing_envelope()
    {
        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("temporarily-unavailable", 100, (_, _) =>
                    throw new HttpRequestException("provider is temporarily unavailable")));
        });

        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(
            new TestRequest("AAPL", s_range)).ConfigureAwait(true);

        result.Payload.Should().BeEmpty();
        result.Coverage.Status.Should().Be(CompletenessStatus.Missing);
        result.Issues.Should().ContainSingle(issue => issue.Code == IssueCode.PartitionFailed);
    }

    [Fact]
    public async Task Public_execute_async_null_request_throws_argument_null_exception_before_resolution()
    {
        var (pipeline, _, _, _, _, _) = BuildPipeline();

        var act = async () => await pipeline.ExecuteAsync<TestRequest, TestRecord>(null!).ConfigureAwait(true);

        var exception = await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
        exception.Which.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task Public_execute_async_preserves_cancellation()
    {
        var (pipeline, _, _, _, _, _) = BuildPipeline();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await pipeline.ExecuteAsync<TestRequest, TestRecord>(
            new TestRequest("AAPL", s_range), cancellation.Token).ConfigureAwait(true);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
    }

    // Test 9
    [Fact]
    public async Task No_serializer_skips_L2()
    {
        // No WriteThroughCache registered
        var (pipeline, _, _, l1, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (_, _) =>
                    Task.FromResult(MakeEnvelope(new TestRecord("AAPL", 42m)))));
        });

        // Act
        var request = new TestRequest("AAPL", s_range);
        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert — should work fine with L1 only
        result.Payload.Should().HaveCount(1);

        var cacheKey = Cache.Keys.CacheKeyBuilder.FromDateRange("pipeline-v2/TestRecord/Daily", "AAPL", s_range.From, s_range.To);
        var cached = await l1.GetAsync<DataEnvelope<IReadOnlyList<TestRecord>>>(cacheKey).ConfigureAwait(true);
        cached.Should().NotBeNull();
    }

    // Test 10
    [Fact]
    public async Task No_normalizer_passes_records_through()
    {
        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (_, _) =>
                    Task.FromResult(MakeEnvelope(new TestRecord("aapl", 100m)))));
            // No INormalizer registered
        });

        // Act
        var request = new TestRequest("aapl", s_range);
        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert — records unchanged (lowercase preserved)
        result.Payload[0].Symbol.Should().Be("aapl");
    }

    // Test 11
    [Fact]
    public async Task No_partitioner_single_partition()
    {
        var fetchCount = 0;
        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (_, _) =>
                {
                    fetchCount++;
                    return Task.FromResult(MakeEnvelope(new TestRecord("AAPL", 100m)));
                }));
            // No IRequestPartitioner registered
        });

        // Act
        var request = new TestRequest("AAPL", s_range);
        await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert — single fetch call
        fetchCount.Should().Be(1);
    }

    // Test 12
    [Fact]
    public async Task Event_log_audit_trail()
    {
        var (pipeline, _, _, _, _, eventLog) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("my-source", 100, (_, _) =>
                    Task.FromResult(MakeEnvelope(new TestRecord("AAPL", 100m)))));
            svc.AddSingleton<INormalizer<TestRecord, TestRecord>>(new UpperCaseNormalizer());
        });

        // Act
        var request = new TestRequest("AAPL", s_range);
        await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert — should have Fetched → Normalized (no Cached since no L2)
        var events = new List<IngestionEvent>();
        await foreach (var (_, evt) in eventLog.ReadFromAsync(0).ConfigureAwait(true))
        {
            events.Add(evt);
        }

        events.Should().HaveCountGreaterThanOrEqualTo(2);
        events[0].Action.Should().Be(IngestionAction.Fetched);
        events[1].Action.Should().Be(IngestionAction.Normalized);
    }

    [Fact]
    public async Task Adapter_originated_cancellation_propagates_without_trying_fallback()
    {
        var fallbackCalls = 0;
        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("cancelled", 100, (_, _) =>
                    throw new OperationCanceledException("adapter operation cancelled")));
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("fallback", 0, (_, _) =>
                {
                    fallbackCalls++;
                    return Task.FromResult(MakeEnvelope(new TestRecord("AAPL", 100m)));
                }));
        });

        await FluentActions.Invoking(() => pipeline.ExecuteAsync<TestRequest, TestRecord>(
                new TestRequest("AAPL", s_range)))
            .Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
        fallbackCalls.Should().Be(0);
    }

    // Test 13
    [Fact]
    public async Task Cancellation_propagates()
    {
        var (pipeline, _, _, _, _, eventLog) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, async (_, ct) =>
                {
                    await Task.Delay(5000, ct).ConfigureAwait(false);
                    return MakeEnvelope(new TestRecord("AAPL", 100m));
                }));
        });

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        // Act & Assert
        var request = new TestRequest("AAPL", s_range);
        await FluentActions
            .Invoking(() => pipeline.ExecuteAsync<TestRequest, TestRecord>(request, cts.Token))
            .Should()
            .ThrowAsync<OperationCanceledException>().ConfigureAwait(true);

        // No Failed event should be logged
        var events = new List<IngestionEvent>();
        await foreach (var (_, evt) in eventLog.ReadFromAsync(0).ConfigureAwait(true))
        {
            events.Add(evt);
        }

        events.Should().NotContain(e => e.Action == IngestionAction.Failed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Failed_partitions_remain_incomplete_and_retryable(bool continueOnFailure)
    {
        var failing = true;
        var partitions = new[] { new TestRequest("P1", s_range), new TestRequest("P2", s_range), new TestRequest("P3", s_range) };
        var (pipeline, _, _, _, _, _) = BuildPipeline(services =>
        {
            services.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (request, _) => failing && request.DatasetKey == "P2"
                    ? throw new IOException("unavailable")
                    : Task.FromResult(MakeEnvelope(new TestRecord(request.DatasetKey, 1m)))));
            services.AddSingleton<IRequestPartitioner<TestRequest>>(new TestPartitioner(partitions));
        }, new DataPipelineOptions { ContinueOnPartitionFailure = continueOnFailure });
        var request = new TestRequest("all", s_range);

        var failed = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        failed.Coverage.Status.Should().Be(CompletenessStatus.Partial);
        failed.Coverage.MissingPoints.Should().BeGreaterThanOrEqualTo(continueOnFailure ? 1 : 2);
        failing = false;
        var repaired = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);
        repaired.Payload.Select(record => record.Symbol).Should().Equal("P1", "P2", "P3");
        repaired.Coverage.Status.Should().Be(CompletenessStatus.Complete);
        repaired.Issues.Should().BeEmpty();
    }

    // Test 14
    [Fact]
    public async Task ContinueOnPartitionFailure_false_stops_early()
    {
        var fetchedPartitions = new List<string>();
        var partitions = new List<TestRequest>
        {
            new("P1", s_range),
            new("P2", s_range),
            new("P3", s_range),
        };

        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (req, _) =>
                {
                    fetchedPartitions.Add(req.DatasetKey);
                    if (req.DatasetKey == "P2")
                    {
                        throw new InvalidOperationException("P2 failed");
                    }

                    return Task.FromResult(MakeEnvelope(new TestRecord(req.DatasetKey, 1m)));
                }));
            svc.AddSingleton<IRequestPartitioner<TestRequest>>(new TestPartitioner(partitions));
        }, new DataPipelineOptions { ContinueOnPartitionFailure = false });

        // Act
        var request = new TestRequest("test", s_range);
        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert — P3 should NOT be attempted
        fetchedPartitions.Should().Contain("P1");
        fetchedPartitions.Should().Contain("P2");
        fetchedPartitions.Should().NotContain("P3");
    }

    // Test 15
    [Fact]
    public async Task Normalizer_transforms_records()
    {
        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (_, _) =>
                    Task.FromResult(MakeEnvelope(
                        new TestRecord("aapl", 100m),
                        new TestRecord("msft", 200m)))));
            svc.AddSingleton<INormalizer<TestRecord, TestRecord>>(new UpperCaseNormalizer());
        });

        // Act
        var request = new TestRequest("mixed", s_range);
        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert — normalizer should uppercase symbols
        result.Payload.Should().HaveCount(2);
        result.Payload[0].Symbol.Should().Be("AAPL");
        result.Payload[1].Symbol.Should().Be("MSFT");
    }

    // Test 16
    [Fact]
    public async Task ContinueOnPartitionFailure_true_processes_remaining()
    {
        var fetchedPartitions = new List<string>();
        var partitions = new List<TestRequest>
        {
            new("P1", s_range),
            new("P2", s_range),
            new("P3", s_range),
        };

        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (req, _) =>
                {
                    fetchedPartitions.Add(req.DatasetKey);
                    if (req.DatasetKey == "P2")
                    {
                        throw new InvalidOperationException("P2 failed");
                    }

                    return Task.FromResult(MakeEnvelope(new TestRecord(req.DatasetKey, 1m)));
                }));
            svc.AddSingleton<IRequestPartitioner<TestRequest>>(new TestPartitioner(partitions));
        }, new DataPipelineOptions { ContinueOnPartitionFailure = true });

        // Act
        var request = new TestRequest("test", s_range);
        var result = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert — P3 SHOULD be attempted despite P2 failure
        fetchedPartitions.Should().HaveCount(3);
        result.Payload.Should().HaveCount(2); // P1 + P3 succeed
        result.Issues.Should().Contain(i => i.Code == IssueCode.PartitionFailed);
    }

    // Test 17
    [Fact]
    public async Task L2_write_through_persists_after_fetch()
    {
        var sharedSnapshotStore = new InMemorySnapshotStore();

        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (_, _) =>
                    Task.FromResult(MakeEnvelope(new TestRecord("AAPL", 42m)))));

            svc.AddSingleton(
                new WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<TestRecord>>>(
                    sharedSnapshotStore,
                    new TestRecordSerializer(),
                    key => key));
        });

        // Act
        var request = new TestRequest("AAPL", s_range);
        await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);

        // Assert — snapshot should now exist in L2
        var cacheKey = Cache.Keys.CacheKeyBuilder.FromDateRange("pipeline-v2/TestRecord/Daily", "AAPL", s_range.From, s_range.To);
        var exists = await sharedSnapshotStore.ExistsAsync(cacheKey).ConfigureAwait(true);
        exists.Should().BeTrue();
    }

    // Test 18
    [Fact]
    public async Task Second_call_hits_L1_returns_cache_retrieval_mode()
    {
        var fetchCount = 0;
        var (pipeline, _, _, _, _, _) = BuildPipeline(svc =>
        {
            svc.AddSingleton<IDataSourceAdapter<TestRequest, TestRecord>>(
                new FakeAdapter("test", 100, (_, _) =>
                {
                    fetchCount++;
                    return Task.FromResult(MakeEnvelope(new TestRecord("AAPL", 100m)));
                }));
        });

        var request = new TestRequest("AAPL", s_range);

        // First call — fetches from adapter
        var result1 = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);
        fetchCount.Should().Be(1);
        result1.Provenance.Should().Contain(p => p.RetrievalMode == RetrievalMode.Api);

        // Second call — should hit L1
        var result2 = await pipeline.ExecuteAsync<TestRequest, TestRecord>(request).ConfigureAwait(true);
        fetchCount.Should().Be(1); // adapter NOT called again
        result2.Provenance.Should().OnlyContain(p => p.RetrievalMode == RetrievalMode.Cache);
    }
}

internal sealed class BatchPartitioner : IRequestPartitioner<BatchTestRequest>
{
    public IReadOnlyList<BatchTestRequest> Partition(BatchTestRequest request) =>
        request.Symbols.Select(s =>
            new BatchTestRequest([s], request.Range, request.Frequency)).ToList();
}
