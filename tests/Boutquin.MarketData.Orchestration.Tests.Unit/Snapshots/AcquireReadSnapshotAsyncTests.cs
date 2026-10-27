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

using System.Globalization;

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.Memory;
using Boutquin.MarketData.Cache.Policies;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.Calendars;
using Boutquin.MarketData.Orchestration.Coverage;
using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.MarketData.Orchestration.Snapshots;
using Boutquin.MarketData.Storage.Checkpointing;
using Boutquin.MarketData.Storage.Events;
using Boutquin.MarketData.Storage.InMemory;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.LsmTree;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;
using Boutquin.Storage.Infrastructure.Events;
using Boutquin.Storage.Infrastructure.LsmTree;
using Boutquin.Storage.Infrastructure.Streaming.Checkpointing;
using Boutquin.Storage.Infrastructure.WriteAheadLog;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Boutquin.MarketData.Orchestration.Tests.Unit.Snapshots;

// M-4.5 deliverable 17 — AcquireReadSnapshotAsync wires the ITimeSeriesStoreRegistry (+ per-record
// snapshot bindings) into a fresh DataSnapshot. This test proves the pipeline factory hands back a
// usable handle that round-trips a read through a real engine snapshot resolved via ResolveEngine.
public sealed class AcquireReadSnapshotAsyncTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "md-acq-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task AcquireReadSnapshotAsync_ReturnsHandleWiredToRegistry_RoundTripsRead()
    {
        await using var engine = await BuildRowEngineAsync();
        var ds = new TestKey(1);
        await engine.SetAsync(new(ds, Day(5)), new TestRecord(Day(5), 50m));
        await engine.SetAsync(new(ds, Day(6)), new TestRecord(Day(6), 60m));

        var registry = new DefaultTimeSeriesStoreRegistry();
        registry.RegisterEngine<TestKey, TestRecord>(engine);

        var pipeline = BuildPipeline(svc =>
        {
            svc.AddSingleton<ITimeSeriesStoreRegistry>(registry);
            svc.AddSingleton<ISnapshotReadBinding<TestRecord>>(new TestBinding());
        });

        await using var snap = await pipeline.AcquireReadSnapshotAsync();

        snap.Should().NotBeNull();
        var env = await snap.ExecuteAsync<TestRecord>(
            new TestRequest(ds, new DateRange(Day(5), Day(6))));

        env.Payload.Select(r => r.Value).Should().Equal(50m, 60m);
    }

    // --- helpers -------------------------------------------------------------------------

    private static DateOnly Day(int d) => new(2024, 1, d);

    private IDataPipeline BuildPipeline(Action<IServiceCollection> configureServices)
    {
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
        configureServices(services);
        var sp = services.BuildServiceProvider();

        return new DataPipeline(
            l1, snapshotStore, checkpointStore, eventLog,
            stalenessPolicy, coverageCalc, opts, sp, new SystemClock(),
            NullLogger<DataPipeline>.Instance);
    }

    private async Task<LsmStorageEngine<TimeSeriesKey<TestKey>, TestRecord>> BuildRowEngineAsync()
    {
        var segmentFolder = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        var walFolder = Path.Combine(segmentFolder, "wal");
        var wal = new WriteAheadLog<TimeSeriesKey<TestKey>, TestRecord>(walFolder);
        var format = new SstSegmentFormat<TimeSeriesKey<TestKey>, TestRecord>();
        var options = new LsmStorageEngineOptions<TimeSeriesKey<TestKey>, TestRecord>(
            MemTableCapacity: 1024,
            SegmentFolder: segmentFolder,
            SegmentPrefix: "ts",
            SegmentFormat: format,
            WriteAheadLog: wal);
        return await LsmStorageEngine<TimeSeriesKey<TestKey>, TestRecord>.OpenAsync(options);
    }

    // --- test doubles --------------------------------------------------------------------

    private sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private readonly record struct TestKey(int Value) : IComparable<TestKey>, ISerializable<TestKey>
    {
        public int CompareTo(TestKey other) => Value.CompareTo(other.Value);

        public void Serialize(Stream stream) => new BinaryWriter(stream).Write(Value);

        public static TestKey Deserialize(Stream stream) => new(new BinaryReader(stream).ReadInt32());
    }

    private readonly record struct TestRecord(DateOnly Date, decimal Value)
        : IHasDate, ISerializable<TestRecord>, IColumnarRehydratable<TestRecord>
    {
        public void Serialize(Stream stream)
        {
            var w = new BinaryWriter(stream);
            w.Write(Date.DayNumber);
            w.Write(Value);
        }

        public static TestRecord Deserialize(Stream stream)
        {
            var r = new BinaryReader(stream);
            var day = DateOnly.FromDayNumber(r.ReadInt32());
            var value = r.ReadDecimal();
            return new(day, value);
        }

        public static TestRecord Rehydrate(IReadOnlyDictionary<string, Array> columns, int rowIndex)
            => new(((DateOnly[])columns[nameof(Date)])[rowIndex], ((decimal[])columns[nameof(Value)])[rowIndex]);
    }

    private sealed record TestRequest(TestKey Dataset, DateRange Range) : IDataRequest
    {
        public string DatasetKey => Dataset.Value.ToString(CultureInfo.InvariantCulture);

        public DataFrequency Frequency => DataFrequency.Daily;
    }

    private sealed class TestBinding : ISnapshotReadBinding<TestRecord>
    {
        public async Task<IAsyncDisposable> CaptureAsync(ITimeSeriesStoreRegistry registry, CancellationToken cancellationToken)
        {
            var engine = registry.ResolveEngine<TestKey, TestRecord>();
            return await engine.AcquireSnapshotAsync(cancellationToken);
        }

        public async Task<SnapshotReadResult<TestRecord>> ReadAsync(
            IAsyncDisposable snapshot, IDataRequest request, CancellationToken cancellationToken)
        {
            var snap = (ISnapshot<TimeSeriesKey<TestKey>, TestRecord>)snapshot;
            var ds = new TestKey(int.Parse(request.DatasetKey, CultureInfo.InvariantCulture));
            var from = request.Range.From;
            var to = request.Range.To;

            var records = new List<TestRecord>();
            await foreach (var kv in snap.GetRangeAsync(new(ds, from), new(ds, to), cancellationToken))
            {
                records.Add(kv.Value);
            }

            var expected = to.DayNumber - from.DayNumber + 1;
            return new SnapshotReadResult<TestRecord>(records, records.Count < expected);
        }
    }
}
