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

using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries.Columnar;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit.TimeSeries;

// M-4 registry tests. Uses purpose-built TestKey / TestRecord types that hand-implement
// ISerializable<T> + IComparable<T> + IHasDate so the tests don't depend on the record-side
// generator emitting ISerializable (which is blocked on DateOnly/decimal primitive-kind
// support in Boutquin.Storage.SourceGenerator — orthogonal follow-up).
public sealed class DefaultTimeSeriesStoreRegistryTests
{
    [Fact]
    public void Resolve_WhenNotRegistered_ReturnsNull()
    {
        var registry = new DefaultTimeSeriesStoreRegistry();

        registry.Resolve<TestKey, TestRecord>().Should().BeNull();
    }

    [Fact]
    public void Register_ThenResolve_ReturnsTheSameInstance()
    {
        var registry = new DefaultTimeSeriesStoreRegistry();
        var store = new FakeStore<TestKey, TestRecord>();

        registry.Register<TestKey, TestRecord>(store);
        var resolved = registry.Resolve<TestKey, TestRecord>();

        resolved.Should().BeSameAs(store);
    }

    [Fact]
    public void Register_WithDistinctTypePairs_KeepsBothRegistrations()
    {
        var registry = new DefaultTimeSeriesStoreRegistry();
        var store1 = new FakeStore<TestKey, TestRecord>();
        var store2 = new FakeStore<OtherKey, OtherRecord>();

        registry.Register<TestKey, TestRecord>(store1);
        registry.Register<OtherKey, OtherRecord>(store2);

        registry.Resolve<TestKey, TestRecord>().Should().BeSameAs(store1);
        registry.Resolve<OtherKey, OtherRecord>().Should().BeSameAs(store2);
    }

    [Fact]
    public void Register_SecondCall_SameTypePair_ReplacesPriorRegistration()
    {
        var registry = new DefaultTimeSeriesStoreRegistry();
        var first = new FakeStore<TestKey, TestRecord>();
        var second = new FakeStore<TestKey, TestRecord>();

        registry.Register<TestKey, TestRecord>(first);
        registry.Register<TestKey, TestRecord>(second);

        registry.Resolve<TestKey, TestRecord>().Should().BeSameAs(second);
    }

    [Fact]
    public void Register_NullStore_Throws()
    {
        var registry = new DefaultTimeSeriesStoreRegistry();

        var act = () => registry.Register<TestKey, TestRecord>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // --- M-4.5: ResolveEngine / RegisterEngine (deliverable 18) -------------------------

    [Fact]
    public void RegisterEngine_ThenResolveEngine_ReturnsTheSameInstance()
    {
        var registry = new DefaultTimeSeriesStoreRegistry();
        var engine = new FakeEngine<TestKey, TestRecord>();

        registry.RegisterEngine<TestKey, TestRecord>(engine);
        var resolved = registry.ResolveEngine<TestKey, TestRecord>();

        resolved.Should().BeSameAs(engine);
    }

    [Fact]
    public void ResolveEngine_WhenNotRegistered_ThrowsInvalidOperationExceptionNamingRecord()
    {
        var registry = new DefaultTimeSeriesStoreRegistry();

        var act = () => { _ = registry.ResolveEngine<TestKey, TestRecord>(); };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(TestRecord).FullName}*");
    }

    [Fact]
    public void RegisterEngine_WithDistinctTypePairs_KeepsBothRegistrations()
    {
        var registry = new DefaultTimeSeriesStoreRegistry();
        var engine1 = new FakeEngine<TestKey, TestRecord>();
        var engine2 = new FakeEngine<OtherKey, OtherRecord>();

        registry.RegisterEngine<TestKey, TestRecord>(engine1);
        registry.RegisterEngine<OtherKey, OtherRecord>(engine2);

        registry.ResolveEngine<TestKey, TestRecord>().Should().BeSameAs(engine1);
        registry.ResolveEngine<OtherKey, OtherRecord>().Should().BeSameAs(engine2);
    }

    [Fact]
    public void RegisterEngine_NullEngine_Throws()
    {
        var registry = new DefaultTimeSeriesStoreRegistry();

        var act = () => registry.RegisterEngine<TestKey, TestRecord>(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // Test doubles -----------------------------------------------------------------------

    // Minimal IComparable+ISerializable key for the registry tests.
    private readonly record struct TestKey(int Value) : IComparable<TestKey>, ISerializable<TestKey>
    {
        public int CompareTo(TestKey other) => Value.CompareTo(other.Value);

        public void Serialize(Stream stream) => new BinaryWriter(stream).Write(Value);

        public static TestKey Deserialize(Stream stream) => new(new BinaryReader(stream).ReadInt32());
    }

    private readonly record struct OtherKey(int Value) : IComparable<OtherKey>, ISerializable<OtherKey>
    {
        public int CompareTo(OtherKey other) => Value.CompareTo(other.Value);

        public void Serialize(Stream stream) => new BinaryWriter(stream).Write(Value);

        public static OtherKey Deserialize(Stream stream) => new(new BinaryReader(stream).ReadInt32());
    }

    private readonly record struct TestRecord(DateOnly Date)
        : IHasDate, ISerializable<TestRecord>, IColumnarRehydratable<TestRecord>
    {
        public void Serialize(Stream stream) => new BinaryWriter(stream).Write(Date.DayNumber);

        public static TestRecord Deserialize(Stream stream)
            => new(DateOnly.FromDayNumber(new BinaryReader(stream).ReadInt32()));

        public static TestRecord Rehydrate(IReadOnlyDictionary<string, Array> columns, int rowIndex)
            => new(((DateOnly[])columns[nameof(Date)])[rowIndex]);
    }

    private readonly record struct OtherRecord(DateOnly Date)
        : IHasDate, ISerializable<OtherRecord>, IColumnarRehydratable<OtherRecord>
    {
        public void Serialize(Stream stream) => new BinaryWriter(stream).Write(Date.DayNumber);

        public static OtherRecord Deserialize(Stream stream)
            => new(DateOnly.FromDayNumber(new BinaryReader(stream).ReadInt32()));

        public static OtherRecord Rehydrate(IReadOnlyDictionary<string, Array> columns, int rowIndex)
            => new(((DateOnly[])columns[nameof(Date)])[rowIndex]);
    }

    // Minimal ITimeSeriesStore<> stub — not used functionally, only for identity comparisons.
    private sealed class FakeStore<TDatasetKey, TRecord> : ITimeSeriesStore<TDatasetKey, TRecord>
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new()
    {
        public Task AppendAsync(TDatasetKey dataset, IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async IAsyncEnumerable<TRecord> GetRangeAsync(TDatasetKey dataset, DateOnly from, DateOnly to, ColumnProjection? projection = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<TRecord> GetTailAsync(TDatasetKey dataset, int count, DateOnly? onOrBefore = null, ColumnProjection? projection = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<DateRange?> GetCoverageAsync(TDatasetKey dataset, CancellationToken cancellationToken = default)
            => Task.FromResult<DateRange?>(null);

        public Task<IReadOnlyList<DateRange>> GetGapsAsync(TDatasetKey dataset, DateRange requested, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DateRange>>(Array.Empty<DateRange>());

        public Task DeleteRangeAsync(TDatasetKey dataset, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    // Minimal ILsmStorageEngine stub — only used for identity comparisons in the engine-registry
    // tests. The composite key is TimeSeriesKey<TDatasetKey>, matching the ResolveEngine contract.
    private sealed class FakeEngine<TDatasetKey, TRecord>
        : ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new()
    {
        public int SegmentCount => 0;

        public bool SupportsSnapshots => false;

        public Task SetAsync(TimeSeriesKey<TDatasetKey> key, TRecord value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<(TRecord Value, bool Found)> TryGetValueAsync(TimeSeriesKey<TDatasetKey> key, CancellationToken cancellationToken = default)
            => Task.FromResult<(TRecord, bool)>((default!, false));

        public Task<bool> ContainsKeyAsync(TimeSeriesKey<TDatasetKey> key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task RemoveAsync(TimeSeriesKey<TDatasetKey> key, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ClearAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IEnumerable<(TimeSeriesKey<TDatasetKey> Key, TRecord Value)>> GetAllItemsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Enumerable.Empty<(TimeSeriesKey<TDatasetKey>, TRecord)>());

        public Task SetBulkAsync(IEnumerable<KeyValuePair<TimeSeriesKey<TDatasetKey>, TRecord>> items, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CompactAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public async IAsyncEnumerable<(TimeSeriesKey<TDatasetKey> Key, TRecord Value)> GetRangeAsync(
            TimeSeriesKey<TDatasetKey> startKey,
            TimeSeriesKey<TDatasetKey> endKey,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<ISnapshot<TimeSeriesKey<TDatasetKey>, TRecord>> AcquireSnapshotAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("FakeEngine does not support snapshots.");
    }
}
