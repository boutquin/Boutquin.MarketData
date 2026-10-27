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
using System.Text;

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Observability;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.DependencyInjection;

using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration;

// This file is copied unchanged into the cold package-consumer lane.
// It uses public APIs only and exercises persisted custom state in both formats.
public sealed class PackagedBloomContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Public_custom_filter_contract_survives_reopening(bool row)
    {
        var directory = Path.Combine(Path.GetTempPath(), "md-package-bloom-" + Guid.NewGuid().ToString("N"));
        var codec = new StateCodec();
        var key = new PriceBarDatasetKey("fixture", DataFrequency.Daily, "AGG", null);
        var date = new DateOnly(2024, 2, 28);
        TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar> Builder()
        {
            var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar").WithSegmentRoot(directory);
            return row ? builder.UseRowFormat() : builder.UseColumnarFormat();
        }

        try
        {
            var stack = await Builder().WithBloomFilter(
                new TimeSeriesBloomFilterOptions { ExpectedItems = 31, FalsePositiveRate = 0.02 })
                .WithBloomFilter(options =>
                {
                    options.ExpectedItems.Should().Be(31);
                    options.FalsePositiveRate.Should().Be(0.02);
                    Interlocked.Increment(ref codec.Creates);
                    return new StateFilter([]);
                }, codec).BuildStackAsync();
            await using (var owner = (IAsyncDisposable)stack.Engine)
            {
                await stack.Store.AppendAsync(key, [
                    new Bar(date, 1, 2, 1, 2, 2, 10),
                    new Bar(date.AddDays(2), 2, 3, 2, 3, 3, 20),
                ]);
                await stack.Engine.FlushAsync();
            }
            codec.Creates.Should().Be(1);
            var reads = codec.Reads;
            var reopened = await Builder().WithBloomFilterStateCodec(codec).BuildStackAsync();
            await using var reopenedOwner = (IAsyncDisposable)reopened.Engine;
            codec.Reads.Should().BeGreaterThan(reads);
            codec.Creates.Should().Be(1, "reopening must decode state rather than invoke the writer factory");
            (await reopened.Engine.TryGetValueAsync(new(key, date))).Found.Should().BeTrue();
            (await reopened.Engine.TryGetValueAsync(new(key, date.AddDays(1)))).Found.Should().BeFalse();
            (await reopened.Engine.TryGetValueAsync(new(key, date.AddDays(2)))).Found.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class StateCodec : IBloomFilterStateCodec<TimeSeriesKey<PriceBarDatasetKey>>
    {
        public int Creates, Reads;
        public string CodecId => "marketdata.package-contract";
        public int Version => 1;
        public byte[] Serialize(IBloomFilter<TimeSeriesKey<PriceBarDatasetKey>> filter)
            => Encoding.UTF8.GetBytes(string.Join('\n', ((StateFilter)filter).Keys.Order(StringComparer.Ordinal)));

        public IBloomFilter<TimeSeriesKey<PriceBarDatasetKey>> Deserialize(ReadOnlyMemory<byte> state)
        {
            Interlocked.Increment(ref Reads);
            var keys = new UTF8Encoding(false, true).GetString(state.Span).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return new StateFilter(new HashSet<string>(keys, StringComparer.Ordinal));
        }
    }

    private sealed class StateFilter(HashSet<string> keys) : IBloomFilter<TimeSeriesKey<PriceBarDatasetKey>>
    {
        public HashSet<string> Keys { get; } = keys;
        private static string Encode(TimeSeriesKey<PriceBarDatasetKey> key)
        {
            using var stream = new MemoryStream();
            key.Serialize(stream);
            return Convert.ToBase64String(stream.ToArray());
        }

        public void Add(TimeSeriesKey<PriceBarDatasetKey> item) => Keys.Add(Encode(item));
        public bool Contains(TimeSeriesKey<PriceBarDatasetKey> item) => Keys.Contains(Encode(item));
        public void Clear() => Keys.Clear();
        public void Dispose() { }
    }
}
