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

using System.Collections.Concurrent;
using System.Text;

using Boutquin.MarketData.Abstractions.Enums;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Observability;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Orchestration.Pipeline;

using Boutquin.Storage.Domain.Enums;
using Boutquin.Storage.Domain.Exceptions;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;
using Boutquin.Storage.Infrastructure.SortedStringTable;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

public sealed class PersistedBloomIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "md-bloom-" + Guid.NewGuid().ToString("N"));
    private static readonly PriceBarDatasetKey s_dataset = new("fixture", DataFrequency.Daily, "AGG", null);
    private static readonly DateOnly s_first = new(2024, 1, 2);

    private TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar> Builder(bool row)
    {
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar")
            .WithSegmentRoot(_root)
            .WithCompactionStrategy(TimeSeriesCompactionStrategy.Full);
        return row ? builder.UseRowFormat() : builder.UseColumnarFormat();
    }

    private static Bar BarAt(int day) => new(s_first.AddDays(day), 100, 101, 99, 100, 100, 1000);
    private static TimeSeriesKey<PriceBarDatasetKey> Key(int day) => new(s_dataset, s_first.AddDays(day));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomFilters_FlushCompactAndReopenWithoutFactoryOrAdd(bool row)
    {
        var codec = new ExactCodec();
        var settings = new TimeSeriesBloomFilterOptions { ExpectedItems = 47, FalsePositiveRate = 0.03 };
        var builder = Builder(row).WithBloomFilter(settings).WithBloomFilter(codec.Create, codec);
        var stack = await builder.BuildStackAsync();
        await using (var owner = (IAsyncDisposable)stack.Engine)
        {
            // Mutating the builder cannot replace the open engine's captured factory/settings.
            builder.WithBloomFilter(new TimeSeriesBloomFilterOptions { Enabled = false });
            for (var day = 0; day < 8; day += 2)
            {
                await stack.Store.AppendAsync(s_dataset, [BarAt(day)]);
                await stack.Engine.FlushAsync();
            }
            await stack.Engine.CompactAsync();
            codec.Settings.Should().NotBeEmpty().And.OnlyContain(x => x == settings);
            codec.Creates.Should().BeGreaterThanOrEqualTo(4);
            (await stack.Engine.TryGetValueAsync(Key(3))).Found.Should().BeFalse();
            (await stack.Engine.TryGetValueAsync(Key(6))).Found.Should().BeTrue();
        }

        var creates = codec.Creates;
        var adds = codec.Adds;
        var decodes = codec.Decodes;
        var reopened = await Builder(row)
            .WithBloomFilter(new TimeSeriesBloomFilterOptions { Enabled = false })
            .WithBloomFilterStateCodec(codec).BuildStackAsync();
        await using (var owner = (IAsyncDisposable)reopened.Engine)
        {
            codec.Decodes.Should().BeGreaterThan(decodes);
            codec.Creates.Should().Be(creates);
            codec.Adds.Should().Be(adds);
            codec.Probes = 0;
            (await reopened.Engine.TryGetValueAsync(Key(3))).Found.Should().BeFalse();
            codec.Probes.Should().BeGreaterThan(0, "the absent key lies inside the segment bounds");
            (await reopened.Engine.TryGetValueAsync(Key(6))).Found.Should().BeTrue();
            var rows = new List<Bar>();
            await foreach (var bar in reopened.Store.GetRangeAsync(s_dataset, s_first, s_first.AddDays(8)))
            {
                rows.Add(bar);
            }
            rows.Select(x => x.Date).Should().Equal(s_first, s_first.AddDays(2), s_first.AddDays(4), s_first.AddDays(6));
            await reopened.Store.AppendAsync(s_dataset, [BarAt(8)]);
            await reopened.Engine.FlushAsync();
            codec.Creates.Should().Be(creates, "disabled new writes do not call the custom factory");
        }
        codec.Alive.Should().Be(0, "all writer, validation and reader filters are engine-owned");
        codec.Disposed.Should().BeFalse("the codec remains caller-owned");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingCodec_FailsWithoutQuarantiningOrLosingData(bool row)
    {
        var codec = new ExactCodec();
        var stack = await Builder(row).WithBloomFilter(codec.Create, codec).BuildStackAsync();
        await using (var owner = (IAsyncDisposable)stack.Engine)
        {
            await stack.Store.AppendAsync(s_dataset, [BarAt(0), BarAt(2)]);
            await stack.Engine.FlushAsync();
        }
        var files = Directory.GetFiles(_root).Order(StringComparer.Ordinal).ToArray();
        var excluded = 0;
        var missing = () => Builder(row).WithCorruptSegmentRecovery(
            Boutquin.Storage.Domain.Enums.CorruptSegmentRecoveryDisposition.ExcludeAndSurface,
            _ => excluded++).BuildStackAsync();
        await missing.Should().ThrowAsync<UnsupportedBloomFilterCodecException>();
        excluded.Should().Be(0);
        Directory.GetFiles(_root).Order(StringComparer.Ordinal).Should().Equal(files);
        var recovered = await Builder(row).WithBloomFilterStateCodec(codec).BuildStackAsync();
        await using var recoveryOwner = (IAsyncDisposable)recovered.Engine;
        (await recovered.Engine.TryGetValueAsync(Key(0))).Found.Should().BeTrue();
        (await recovered.Engine.TryGetValueAsync(Key(2))).Found.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FactoryWithoutCodec_IsRejectedBeforeCreatingFiles(bool row)
    {
        var codec = new ExactCodec();
        var open = () => Builder(row).WithBloomFilter(codec.Create).BuildStackAsync();
        await open.Should().ThrowAsync<ArgumentException>();
        Directory.Exists(_root).Should().BeFalse();
        codec.Creates.Should().Be(0);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NumericSettings_ReachPhysicalRowFooter(bool rowOnly, bool disabled)
    {
        var settings = new TimeSeriesBloomFilterOptions { ExpectedItems = 73, FalsePositiveRate = 0.02, Enabled = !disabled };
        var pipeline = new DataPipelineOptions { TimeSeriesBloomFilter = settings, TimeSeriesSegmentFormat = TimeSeriesSegmentFormat.Row };
        TimeSeriesStack<PriceBarDatasetKey, Bar> stack;
        if (rowOnly)
        {
            var options = new RowTimeSeriesStoreOptions();
            ServiceCollectionExtensions.ApplyRowStoreShapingOptions(options, _root, pipeline);
            stack = await RowTimeSeriesStoreFactory.BuildStackAsync<PriceBarDatasetKey, Bar>("Bar", options);
        }
        else
        {
            var builder = Builder(true);
            ServiceCollectionExtensions.ApplyStoreShapingOptions(builder, _root, pipeline);
            stack = await builder.BuildStackAsync();
        }
        await using var owner = (IAsyncDisposable)stack.Engine;
        await stack.Store.AppendAsync(s_dataset, [BarAt(0), BarAt(2)]);
        await stack.Engine.FlushAsync();
        var path = Directory.GetFiles(_root, "*.sst").Should().ContainSingle().Which;
        SsTableFooter.TryReadFromEnd(File.ReadAllBytes(path), out var footer).Should().BeTrue();
        var expectedBits = disabled ? 0 : (int)Math.Ceiling(-73 * Math.Log(0.02) / Math.Pow(Math.Log(2), 2));
        footer!.BloomBitsCount.Should().Be(expectedBits);
        (await stack.Engine.TryGetValueAsync(Key(0))).Found.Should().BeTrue();
        (await stack.Engine.TryGetValueAsync(Key(1))).Found.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 0.01)]
    [InlineData(-1, 0.01)]
    [InlineData(100, 0)]
    [InlineData(100, 1)]
    [InlineData(100, double.NaN)]
    [InlineData(100, double.PositiveInfinity)]
    public void InvalidSettings_AreRejectedByBuilder(int count, double probability)
    {
        var configure = () => Builder(false).WithBloomFilter(new TimeSeriesBloomFilterOptions
        {
            ExpectedItems = count,
            FalsePositiveRate = probability,
        });
        configure.Should().Throw<ArgumentOutOfRangeException>();
        Directory.Exists(_root).Should().BeFalse();
    }

    private sealed class ExactCodec : IBloomFilterStateCodec<TimeSeriesKey<PriceBarDatasetKey>>, IDisposable
    {
        public int Creates, Adds, Decodes, Probes, Alive;
        public bool Disposed { get; private set; }
        public ConcurrentQueue<TimeSeriesBloomFilterOptions> Settings { get; } = new();
        public string CodecId => "marketdata.test.exact";
        public int Version => 1;

        public IBloomFilter<TimeSeriesKey<PriceBarDatasetKey>> Create(TimeSeriesBloomFilterOptions settings)
        {
            Interlocked.Increment(ref Creates);
            Settings.Enqueue(settings);
            return new Filter(this, []);
        }

        public byte[] Serialize(IBloomFilter<TimeSeriesKey<PriceBarDatasetKey>> filter)
            => Encoding.UTF8.GetBytes(string.Join('\n', ((Filter)filter).Keys.Order(StringComparer.Ordinal)));

        public IBloomFilter<TimeSeriesKey<PriceBarDatasetKey>> Deserialize(ReadOnlyMemory<byte> state)
        {
            Interlocked.Increment(ref Decodes);
            var text = new UTF8Encoding(false, true).GetString(state.Span);
            return new Filter(this, new HashSet<string>(text.Length == 0 ? [] : text.Split('\n'), StringComparer.Ordinal));
        }

        public void Dispose() => Disposed = true;

        private sealed class Filter : IBloomFilter<TimeSeriesKey<PriceBarDatasetKey>>
        {
            private readonly ExactCodec _owner;
            private bool _disposed;
            public HashSet<string> Keys { get; }

            public Filter(ExactCodec owner, HashSet<string> keys)
            {
                _owner = owner;
                Keys = keys;
                Interlocked.Increment(ref owner.Alive);
            }

            private static string Encode(TimeSeriesKey<PriceBarDatasetKey> key)
            {
                using var stream = new MemoryStream();
                key.Serialize(stream);
                return Convert.ToBase64String(stream.ToArray());
            }

            public void Add(TimeSeriesKey<PriceBarDatasetKey> key)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                Interlocked.Increment(ref _owner.Adds);
                Keys.Add(Encode(key));
            }

            public bool Contains(TimeSeriesKey<PriceBarDatasetKey> key)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                Interlocked.Increment(ref _owner.Probes);
                return Keys.Contains(Encode(key));
            }

            public void Clear() => Keys.Clear();
            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    Interlocked.Decrement(ref _owner.Alive);
                }
            }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
