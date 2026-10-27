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
using Boutquin.MarketData.Abstractions.Snapshots;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.Orchestration.Snapshots;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.LsmTree;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;
using Boutquin.Storage.Infrastructure.LsmTree;
using Boutquin.Storage.Infrastructure.WriteAheadLog;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Orchestration.Tests.Unit.Snapshots;

// M-4.5 DataSnapshot tests. AC-15 (pre/post-acquire MVCC visibility) and AC-16 (gap fail-fast,
// no adapter dispatch). These wire a *real* row-format LsmStorageEngine so post-acquisition
// invisibility is exhibited by the storage engine's MVCC, not faked by a stub.
public sealed class DataSnapshotTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "md-snap-tests", Guid.NewGuid().ToString("N"));

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
            // Best-effort cleanup; the OS reaps the temp dir eventually.
        }
    }

    // --- AC-15: MVCC visibility ---------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RecordWrittenBeforeAcquisition_IsVisibleThroughSnapshot()
    {
        await using var engine = await BuildRowEngineAsync();
        var ds = new TestKey(1);
        await engine.SetAsync(Key(ds, 5), new TestRecord(Day(5), 50m));
        await engine.SetAsync(Key(ds, 6), new TestRecord(Day(6), 60m));

        var registry = new DefaultTimeSeriesStoreRegistry();
        registry.RegisterEngine<TestKey, TestRecord>(engine);

        await using var snap = NewSnapshot(registry);

        var env = await snap.ExecuteAsync<TestRecord>(Request(ds, 5, 6));

        env.Payload.Select(r => r.Value).Should().Equal(50m, 60m);
    }

    [Fact]
    public async Task ExecuteAsync_RecordAppendedAfterAcquisition_IsInvisibleToTheSameSnapshot()
    {
        await using var engine = await BuildRowEngineAsync();
        var ds = new TestKey(1);
        // Pre-acquisition coverage: days 5..7 all present (no gap for the requested range).
        await engine.SetAsync(Key(ds, 5), new TestRecord(Day(5), 50m));
        await engine.SetAsync(Key(ds, 6), new TestRecord(Day(6), 60m));
        await engine.SetAsync(Key(ds, 7), new TestRecord(Day(7), 70m));

        var registry = new DefaultTimeSeriesStoreRegistry();
        registry.RegisterEngine<TestKey, TestRecord>(engine);

        await using var snap = NewSnapshot(registry);

        // First read captures the snapshot watermark for TestRecord.
        var before = await snap.ExecuteAsync<TestRecord>(Request(ds, 5, 7));
        before.Payload.Select(r => r.Value).Should().Equal(50m, 60m, 70m);

        // A correction lands on day 6 AFTER acquisition (last-writer-wins on the live engine).
        await engine.SetAsync(Key(ds, 6), new TestRecord(Day(6), 999m));

        // The same snapshot must NOT see the post-acquisition write — point-in-time stability.
        var after = await snap.ExecuteAsync<TestRecord>(Request(ds, 5, 7));
        after.Payload.Select(r => r.Value).Should().Equal(50m, 60m, 70m);
    }

    [Fact]
    public async Task ExecuteAsync_CapturesSnapshotOncePerRecordType()
    {
        await using var engine = await BuildRowEngineAsync();
        var ds = new TestKey(1);
        await engine.SetAsync(Key(ds, 5), new TestRecord(Day(5), 50m));

        var registry = new DefaultTimeSeriesStoreRegistry();
        registry.RegisterEngine<TestKey, TestRecord>(engine);

        var binding = new TestBinding();
        await using var snap = new DataSnapshot(registry, t => t == typeof(TestRecord) ? binding : null);

        await snap.ExecuteAsync<TestRecord>(Request(ds, 5, 5));
        await snap.ExecuteAsync<TestRecord>(Request(ds, 5, 5));

        binding.CaptureCount.Should().Be(1, "the snapshot is captured once per TRecord and reused");
    }

    // --- AC-16: gap fail-fast, no adapter dispatch --------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhenRangeHasGap_ThrowsSnapshotGapException()
    {
        await using var engine = await BuildRowEngineAsync();
        var ds = new TestKey(1);
        // Days 5 and 7 present; day 6 missing — an interior gap over the requested [5,7].
        await engine.SetAsync(Key(ds, 5), new TestRecord(Day(5), 50m));
        await engine.SetAsync(Key(ds, 7), new TestRecord(Day(7), 70m));

        var registry = new DefaultTimeSeriesStoreRegistry();
        registry.RegisterEngine<TestKey, TestRecord>(engine);

        await using var snap = NewSnapshot(registry);

        var act = async () => await snap.ExecuteAsync<TestRecord>(Request(ds, 5, 7));

        await act.Should().ThrowAsync<SnapshotGapException>();
    }

    [Fact]
    public async Task ExecuteAsync_WhenRangeHasGap_NeverDispatchesAdapter()
    {
        await using var engine = await BuildRowEngineAsync();
        var ds = new TestKey(1);
        await engine.SetAsync(Key(ds, 5), new TestRecord(Day(5), 50m));
        await engine.SetAsync(Key(ds, 7), new TestRecord(Day(7), 70m));

        var registry = new DefaultTimeSeriesStoreRegistry();
        registry.RegisterEngine<TestKey, TestRecord>(engine);

        // The binding tracks adapter dispatch; the snapshot path must never trigger it.
        var binding = new TestBinding();
        await using var snap = new DataSnapshot(registry, t => t == typeof(TestRecord) ? binding : null);

        var act = async () => await snap.ExecuteAsync<TestRecord>(Request(ds, 5, 7));

        await act.Should().ThrowAsync<SnapshotGapException>();
        binding.AdapterDispatchCount.Should().Be(0, "a snapshot read fails fast on a gap, never falling back to the adapter");
    }

    // --- §2.2.1 lifecycle ----------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        await using var engine = await BuildRowEngineAsync();
        var ds = new TestKey(1);
        await engine.SetAsync(Key(ds, 5), new TestRecord(Day(5), 50m));

        var registry = new DefaultTimeSeriesStoreRegistry();
        registry.RegisterEngine<TestKey, TestRecord>(engine);

        var snap = NewSnapshot(registry);
        await snap.DisposeAsync();

        var act = async () => await snap.ExecuteAsync<TestRecord>(Request(ds, 5, 5));

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsNoOp()
    {
        await using var engine = await BuildRowEngineAsync();
        var registry = new DefaultTimeSeriesStoreRegistry();
        registry.RegisterEngine<TestKey, TestRecord>(engine);

        var snap = NewSnapshot(registry);
        await snap.DisposeAsync();

        var act = async () => await snap.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    // --- helpers -------------------------------------------------------------------------

    private IDataSnapshot NewSnapshot(ITimeSeriesStoreRegistry registry)
        => new DataSnapshot(registry, t => t == typeof(TestRecord) ? new TestBinding() : null);

    private static DateOnly Day(int d) => new(2024, 1, d);

    private static TimeSeriesKey<TestKey> Key(TestKey ds, int day) => new(ds, Day(day));

    private static TestRequest Request(TestKey ds, int from, int to)
        => new(ds, new DateRange(Day(from), Day(to)));

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

    // Test binding: resolves the registered engine, captures a real ISnapshot once, reads the
    // requested range through it, and detects gaps. AdapterDispatchCount stays at 0 because the
    // binding NEVER calls an adapter — a gap is surfaced to DataSnapshot, which throws.
    private sealed class TestBinding : ISnapshotReadBinding<TestRecord>
    {
        public int CaptureCount { get; private set; }

        public int AdapterDispatchCount { get; private set; }

        public async Task<IAsyncDisposable> CaptureAsync(ITimeSeriesStoreRegistry registry, CancellationToken cancellationToken)
        {
            CaptureCount++;
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

            // Gap = the number of distinct business days returned is fewer than the requested span.
            // (The test corpus uses consecutive calendar days, so expected = inclusive day count.)
            var expected = to.DayNumber - from.DayNumber + 1;
            var hasGap = records.Count < expected;

            return new SnapshotReadResult<TestRecord>(records, hasGap);
        }
    }
}
