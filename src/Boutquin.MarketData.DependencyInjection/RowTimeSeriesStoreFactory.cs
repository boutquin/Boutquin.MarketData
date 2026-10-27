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

using Boutquin.MarketData.Abstractions.Enums;
using Boutquin.MarketData.Abstractions.Observability;

using Boutquin.Storage.Domain.Enums;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.Compression;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.ValueObjects.LsmTree;
using Boutquin.Storage.Domain.ValueObjects.SortedStringTable;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;
using Boutquin.Storage.Domain.ValueObjects.ValueLog;
using Boutquin.Storage.Infrastructure.LsmTree;
using Boutquin.Storage.Infrastructure.SortedStringTable;
using Boutquin.Storage.Infrastructure.TimeSeries;
using Boutquin.Storage.Infrastructure.WriteAheadLog;

namespace Boutquin.MarketData.DependencyInjection;

/// <summary>
/// Row-format store options for a per-record opt-out registration. Mirrors
/// the row branch of <see cref="TimeSeriesStoreBuilder{TDatasetKey, TRecord}"/> but with the
/// <b>row-safe</b> constraint set — a <c>[StorageSerializable]</c>-only record that does NOT
/// implement <c>IColumnarRehydratable</c> can register a row store because Storage 2.0's Option-A
/// relocation removed that constraint from the format-agnostic <c>LsmTimeSeriesStore</c> /
/// <c>LsmTimeSeriesStorageEngine</c> / base <c>TimeSeriesStoreOptions</c> surfaces.
/// </summary>
public sealed class RowTimeSeriesStoreOptions
{
    /// <summary>
    /// On-disk segment root; the <c>{record-type}</c> token is substituted per store. Standalone
    /// row stores default to <c>.marketdata-timeseries</c> beneath <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string SegmentRoot { get; set; } = Path.Combine(
        AppContext.BaseDirectory,
        ".marketdata-timeseries",
        "{record-type}");

    /// <summary>Compaction strategy. Defaults to <see cref="TimeSeriesCompactionStrategy.SizeTiered"/>.</summary>
    public TimeSeriesCompactionStrategy CompactionStrategy { get; set; } = TimeSeriesCompactionStrategy.SizeTiered;

    /// <summary>Group-commit WAL options. Defaults match the kernel defaults.</summary>
    public WriteAheadLogOptions WriteAheadLog { get; set; } = new()
    {
        GroupCommitBatchSize = 256,
        GroupCommitLatencyMs = 5,
        SegmentMaxBytes = 64L * 1024L * 1024L,
    };

    /// <summary>Settings for physical Bloom filters in newly written and compacted row segments.</summary>
    public TimeSeriesBloomFilterOptions BloomFilter { get; set; } = new();

    /// <summary>Optional WiscKey value-log options; <see langword="null"/> keeps value-log off.</summary>
    public ValueLogOptions? ValueLog { get; set; }

    /// <summary>Optional row-path SSTable block compression codec.</summary>
    public ICompressionCodec? CompressionCodec { get; set; }
}

/// <summary>
/// Fluent configurator for <c>ConfigureTimeSeriesStore&lt;…&gt;</c>. Mirrors the
/// <c>b.UseRowFormat().UseValueLog(opts)</c> pattern: the row format is implied (a
/// <c>ConfigureTimeSeriesStore</c> registration is always a row store), and <c>UseValueLog</c> opts
/// into WiscKey dispatch for a record whose payload exceeds the inline threshold.
/// </summary>
public sealed class RowStoreConfigurator
{
    private readonly RowTimeSeriesStoreOptions _options = new();

    /// <summary>No-op that documents intent — a <c>ConfigureTimeSeriesStore</c> store is always row.</summary>
    public RowStoreConfigurator UseRowFormat() => this;

    /// <summary>Overrides the on-disk segment root.</summary>
    public RowStoreConfigurator WithSegmentRoot(string segmentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentRoot);
        _options.SegmentRoot = segmentRoot;
        return this;
    }

    /// <summary>Selects the compaction strategy.</summary>
    public RowStoreConfigurator WithCompactionStrategy(TimeSeriesCompactionStrategy strategy)
    {
        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unsupported time-series compaction strategy.");
        }

        _options.CompactionStrategy = strategy;
        return this;
    }

    /// <summary>Sizes physical Bloom filters in new row segments.</summary>
    /// <param name="options">Immutable settings; existing segment filters retain their persisted sizing.</param>
    /// <returns>This configurator.</returns>
    /// <exception cref="ArgumentNullException">Options are null.</exception>
    public RowStoreConfigurator WithBloomFilter(TimeSeriesBloomFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options.BloomFilter = options;
        return this;
    }

    /// <summary>
    /// Enables WiscKey value-log dispatch for this row store. Only valid on a row store —
    /// which this always is — so, unlike the columnar builder, it cannot be mis-paired with columnar.
    /// </summary>
    public RowStoreConfigurator UseValueLog(ValueLogOptions valueLog)
    {
        ArgumentNullException.ThrowIfNull(valueLog);
        _options.ValueLog = valueLog;
        return this;
    }

    /// <summary>Materialises the configured options.</summary>
    internal void Apply(RowTimeSeriesStoreOptions target)
    {
        target.SegmentRoot = _options.SegmentRoot;
        target.CompactionStrategy = _options.CompactionStrategy;
        target.WriteAheadLog = _options.WriteAheadLog;
        target.BloomFilter = _options.BloomFilter;
        target.ValueLog = _options.ValueLog;
        target.CompressionCodec = _options.CompressionCodec;
    }
}

/// <summary>
/// Builds a live <b>row-format</b> <see cref="LsmTimeSeriesStore{TDatasetKey, TRecord}"/> for a
/// record that is <c>[StorageSerializable]</c>-only. The columnar builder cannot serve these records
/// (its type parameter demands <c>IColumnarRehydratable</c>); this factory constrains
/// <c>TRecord</c> to the row-safe minimum and threads <c>SstSegmentFormat</c> +
/// <c>TimeSeriesStoreOptions</c> (the format-agnostic base, not the columnar-derived options).
/// </summary>
public static class RowTimeSeriesStoreFactory
{
    private const int DefaultMemTableCapacity = 10_000;
    private const int DefaultFullCompactionThreshold = 4;

    /// <summary>
    /// Assembles and opens a live row-format store + its inner LSM engine for the
    /// <c>(TDatasetKey, TRecord)</c> pair. Directory creation is delegated to Storage's file-backed
    /// primitives (the WAL ctor and <c>LsmStorageEngine.OpenAsync</c>), so this package never
    /// touches <c>System.IO.Directory</c> directly.
    /// </summary>
    public static async Task<TimeSeriesStack<TDatasetKey, TRecord>> BuildStackAsync<TDatasetKey, TRecord>(
        string recordTypeToken,
        RowTimeSeriesStoreOptions options,
        CancellationToken cancellationToken = default)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordTypeToken);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.CompactionStrategy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.CompactionStrategy,
                "Unsupported time-series compaction strategy.");
        }

        ArgumentNullException.ThrowIfNull(options.BloomFilter);
        var bloomOptions = new SegmentBloomFilterOptions<TimeSeriesKey<TDatasetKey>>(
            options.BloomFilter.Enabled, options.BloomFilter.ExpectedItems, options.BloomFilter.FalsePositiveRate);
        var segmentFolder = options.SegmentRoot.Replace("{record-type}", recordTypeToken, StringComparison.Ordinal);
        var walFolder = Path.Combine(segmentFolder, "wal");

        var wal = new WriteAheadLog<TimeSeriesKey<TDatasetKey>, TRecord>(walFolder, options.WriteAheadLog);
        WalOwningLsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>? owningEngine = null;
        try
        {
            var compaction = ResolveCompaction<TDatasetKey>(options.CompactionStrategy);

            var segmentFormat = new SstSegmentFormat<TimeSeriesKey<TDatasetKey>, TRecord>(
                bloomOptions, options.CompressionCodec,
                SortedStringTable<TimeSeriesKey<TDatasetKey>, TRecord>.DefaultBlockSizeBytes,
                SortedStringTable<TimeSeriesKey<TDatasetKey>, TRecord>.DefaultRestartInterval,
                null, null);
            var tsOptions = new TimeSeriesStoreOptions<TRecord>
            {
                SegmentFormat = TimeSeriesSegmentFormat.Row,
            };

            var lsmOptions = new LsmStorageEngineOptions<TimeSeriesKey<TDatasetKey>, TRecord>(
                DefaultMemTableCapacity,
                segmentFolder,
                SegmentPrefix: "ts",
                segmentFormat,
                wal,
                compaction,
                options.ValueLog)
            {
                CorruptSegmentRecovery = CorruptSegmentRecoveryDisposition.ExcludeAndSurface,
            };

            var innerEngine = await LsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>
                .OpenAsync(lsmOptions, cancellationToken).ConfigureAwait(false);
            owningEngine = new WalOwningLsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>(innerEngine, wal);
            var tsEngine = new LsmTimeSeriesStorageEngine<TDatasetKey, TRecord>(owningEngine, tsOptions);
            var coverage = new InMemoryDatasetCoverageIndex<TDatasetKey>();
            var store = new LsmTimeSeriesStore<TDatasetKey, TRecord>(tsEngine, coverage);

            return new TimeSeriesStack<TDatasetKey, TRecord>(store, owningEngine);
        }
        catch (Exception ex)
        {
            WalOwningLsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>
                .DisposeAfterBuildFailure(owningEngine, wal, ex);
            throw;
        }
    }

    private static ICompactionStrategy<TimeSeriesKey<TDatasetKey>> ResolveCompaction<TDatasetKey>(
        TimeSeriesCompactionStrategy strategy)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        => strategy switch
        {
            TimeSeriesCompactionStrategy.SizeTiered => new SizeTieredCompactionStrategy<TimeSeriesKey<TDatasetKey>>(),
            TimeSeriesCompactionStrategy.Leveled => new LeveledCompactionStrategy<TimeSeriesKey<TDatasetKey>>(),
            TimeSeriesCompactionStrategy.Full => new FullCompactionStrategy<TimeSeriesKey<TDatasetKey>>(DefaultFullCompactionThreshold),
            _ => throw new InvalidOperationException($"Unsupported time-series compaction strategy: {strategy}."),
        };
}
