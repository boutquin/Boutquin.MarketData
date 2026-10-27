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
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.LsmTree;
using Boutquin.Storage.Domain.ValueObjects.SortedStringTable;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;
using Boutquin.Storage.Domain.ValueObjects.ValueLog;
using Boutquin.Storage.Infrastructure.DataStructures;
using Boutquin.Storage.Infrastructure.LsmTree;
using Boutquin.Storage.Infrastructure.SortedStringTable;
using Boutquin.Storage.Infrastructure.TimeSeries;
using Boutquin.Storage.Infrastructure.TimeSeries.Columnar;
using Boutquin.Storage.Infrastructure.WriteAheadLog;

namespace Boutquin.MarketData.DependencyInjection;

/// <summary>
/// Fluent builder that assembles a live columnar (or row-format) <see cref="ITimeSeriesStore{TDatasetKey, TRecord}"/>
/// for a single dated record type, threading the full store-shaping option surface into a Boutquin.Storage
/// <see cref="LsmStorageEngine{TKey, TValue}"/> + <see cref="LsmTimeSeriesStorageEngine{TDatasetKey, TRecord}"/>
/// + <see cref="LsmTimeSeriesStore{TDatasetKey, TRecord}"/> stack.
/// </summary>
/// <typeparam name="TDatasetKey">Dataset partition key type.</typeparam>
/// <typeparam name="TRecord">
/// Dated record type. Must be <c>[ColumnarStorable]</c> — the source generator emits the
/// <see cref="IColumnarRehydratable{TSelf}"/> implementation the Storage LSM time-series stack
/// requires on every record, regardless of whether the runtime segment format is columnar or row.
/// </typeparam>
/// <remarks>
/// <para><b>WiscKey guardrail.</b> <see cref="UseValueLog(Action{ValueLogOptionsBuilder})"/> is
/// valid only after <see cref="UseRowFormat"/>; columnar segments reject non-inline entries, so
/// enabling value-log dispatch on a columnar store fails fast at configuration time.</para>
/// <para><b>Bloom filter.</b> Settings apply to every new row or columnar segment, including
/// compaction output. Custom factories require a state codec so reopening loads the persisted
/// filter without rebuilding it. Storage owns factory and decoder results; the caller owns the codec.</para>
/// </remarks>
public sealed class TimeSeriesStoreBuilder<TDatasetKey, TRecord>
    where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
{
    private const int DefaultMemTableCapacity = 10_000;
    private const int DefaultRowGroupMaxRows = 1 << 17;
    private const int DefaultFullCompactionThreshold = 4;

    private readonly string _recordTypeToken;

    private string _segmentRoot = Path.Combine(
        AppContext.BaseDirectory,
        ".marketdata-timeseries",
        "{record-type}");
    private IDatasetCoverageIndex<TDatasetKey>? _coverageIndex;
    private WriteAheadLogOptions _walOptions = new()
    {
        GroupCommitBatchSize = 256,
        GroupCommitLatencyMs = 5,
        SegmentMaxBytes = 64L * 1024L * 1024L,
    };

    private TimeSeriesBloomFilterOptions _bloomOptions = new();
    private Func<TimeSeriesBloomFilterOptions, IBloomFilter<TimeSeriesKey<TDatasetKey>>> _bloomFactory =
        opts => BloomFilter.ForSerializable<TimeSeriesKey<TDatasetKey>>(opts.ExpectedItems, opts.FalsePositiveRate);

    private bool _customBloomFactory;
    private IBloomFilterStateCodec<TimeSeriesKey<TDatasetKey>>? _bloomStateCodec;

    private ValueLogOptions _valueLog = ValueLogOptions.Default;
    private bool _valueLogEnabled;
    private ICompressionCodec? _compressionCodec;

    // MarketData opts into ExcludeAndSurface by default: a rebuildable cache
    // self-heals a CRC-mismatched segment by excluding it, firing the callback, and re-fetching the
    // affected span via gap fill rather than failing closed. FailClosed is available for adopters who
    // want the authoritative-store semantics (open throws on a corrupt segment).
    private CorruptSegmentRecoveryDisposition _corruptSegmentRecovery =
        CorruptSegmentRecoveryDisposition.ExcludeAndSurface;
    private Action<CorruptSegmentExclusion>? _onCorruptSegmentExcluded;
    private readonly QuarantineTracker _quarantineTracker = new();

    /// <summary>
    /// Creates a builder for a record type whose <c>{record-type}</c> segment-root token resolves
    /// to <paramref name="recordTypeToken"/>.
    /// </summary>
    /// <param name="recordTypeToken">
    /// Per-record sub-directory name substituted into the <c>{record-type}</c> token of the
    /// segment root (typically <c>nameof(TRecord)</c>).
    /// </param>
    public TimeSeriesStoreBuilder(string recordTypeToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordTypeToken);
        _recordTypeToken = recordTypeToken;
    }

    /// <summary>The selected on-disk segment format. Defaults to <see cref="TimeSeriesSegmentFormat.Columnar"/>.</summary>
    public TimeSeriesSegmentFormat SegmentFormat { get; private set; } = TimeSeriesSegmentFormat.Columnar;

    /// <summary>The selected compaction strategy. Defaults to <see cref="TimeSeriesCompactionStrategy.SizeTiered"/>.</summary>
    public TimeSeriesCompactionStrategy CompactionStrategy { get; private set; } = TimeSeriesCompactionStrategy.SizeTiered;

    /// <summary>The selected quarantine behavior. Defaults to <see cref="QuarantineBehavior.TreatAsGap"/>.</summary>
    public QuarantineBehavior QuarantineBehavior { get; private set; } = QuarantineBehavior.TreatAsGap;

    /// <summary>The configured incident sink, or <see langword="null"/> for the logger fallback.</summary>
    public IDataQualityIncidentSink? DataQualityIncidentSink { get; private set; }

    /// <summary>The configured group-commit WAL options (defaults: 256-append batch, 5 ms, 64 MiB).</summary>
    public WriteAheadLogOptions WriteAheadLogOptions => _walOptions;

    /// <summary>The configured per-segment bloom-filter options (defaults: 131,072 items at 1%).</summary>
    public TimeSeriesBloomFilterOptions BloomFilterOptions => _bloomOptions;

    /// <summary>Whether WiscKey value-log dispatch was explicitly enabled (row format only).</summary>
    public bool ValueLogEnabled => _valueLogEnabled;

    /// <summary>Routes segments to the columnar layout (the library default).</summary>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> UseColumnarFormat()
    {
        SegmentFormat = TimeSeriesSegmentFormat.Columnar;
        return this;
    }

    /// <summary>Routes segments to the row (block-structured SSTable) layout — the per-record opt-out.</summary>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> UseRowFormat()
    {
        SegmentFormat = TimeSeriesSegmentFormat.Row;
        return this;
    }

    /// <summary>Overrides the segment root for this store.</summary>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithSegmentRoot(string segmentRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentRoot);
        _segmentRoot = segmentRoot;
        return this;
    }

    /// <summary>Supplies the coverage index (e.g. an in-memory index for tests).</summary>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithCoverageIndex(IDatasetCoverageIndex<TDatasetKey> coverageIndex)
    {
        ArgumentNullException.ThrowIfNull(coverageIndex);
        _coverageIndex = coverageIndex;
        return this;
    }

    /// <summary>Selects the compaction strategy.</summary>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithCompactionStrategy(TimeSeriesCompactionStrategy strategy)
    {
        if (!Enum.IsDefined(strategy))
        {
            throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unsupported time-series compaction strategy.");
        }

        CompactionStrategy = strategy;
        return this;
    }

    /// <summary>
    /// Selects how the engine treats a manifest-listed segment that fails its integrity check at
    /// open. MarketData defaults to
    /// <see cref="CorruptSegmentRecoveryDisposition.ExcludeAndSurface"/> — the corrupt segment is
    /// excluded, <paramref name="onCorruptSegmentExcluded"/> fires (if supplied), coverage shrinks,
    /// and the affected range re-fetches via gap fill. Pass
    /// <see cref="CorruptSegmentRecoveryDisposition.FailClosed"/> for authoritative-store semantics
    /// (engine open throws an <c>InvalidDataException</c> naming the segment).
    /// </summary>
    /// <param name="disposition">The recovery disposition to apply at engine open.</param>
    /// <param name="onCorruptSegmentExcluded">
    /// Invoked once per segment excluded under <see cref="CorruptSegmentRecoveryDisposition.ExcludeAndSurface"/>;
    /// the consumer's hook for shrinking coverage accounting and scheduling a re-ingest. Ignored under
    /// <see cref="CorruptSegmentRecoveryDisposition.FailClosed"/>.
    /// </param>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithCorruptSegmentRecovery(
        CorruptSegmentRecoveryDisposition disposition,
        Action<CorruptSegmentExclusion>? onCorruptSegmentExcluded = null)
    {
        _corruptSegmentRecovery = disposition;
        _onCorruptSegmentExcluded = onCorruptSegmentExcluded;
        return this;
    }

    /// <summary>The selected corrupt-segment recovery disposition (defaults to ExcludeAndSurface).</summary>
    public CorruptSegmentRecoveryDisposition CorruptSegmentRecovery => _corruptSegmentRecovery;

    /// <summary>Mutates the group-commit WAL options in place.</summary>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithWriteAheadLogOptions(Action<WriteAheadLogOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new WriteAheadLogOptionsBuilder(_walOptions);
        configure(b);
        _walOptions = b.Build();
        return this;
    }

    /// <summary>Sizes physical segment filters and restores the built-in factory for new writes.</summary>
    /// <param name="options">Immutable settings for newly written segments.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">Options are null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The expected count or probability is invalid.</exception>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithBloomFilter(TimeSeriesBloomFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _ = new SegmentBloomFilterOptions<TimeSeriesKey<TDatasetKey>>(
            options.Enabled, options.ExpectedItems, options.FalsePositiveRate);
        _bloomOptions = options;
        _customBloomFactory = false;
        _bloomFactory = opts => BloomFilter.ForSerializable<TimeSeriesKey<TDatasetKey>>(opts.ExpectedItems, opts.FalsePositiveRate);
        return this;
    }

    /// <summary>Sets a custom filter factory. Building a store also requires a state codec.</summary>
    /// <param name="factory">Creates a fresh filter for each segment; Storage owns the returned instances.</param>
    /// <returns>This builder.</returns>
    /// <remarks>Use the codec overload or <see cref="WithBloomFilterStateCodec"/> before building a store.
    /// <see cref="BuildBloomFilter"/> remains available without a codec for standalone use.</remarks>
    /// <exception cref="ArgumentNullException">The factory is null.</exception>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithBloomFilter(
        Func<TimeSeriesBloomFilterOptions, IBloomFilter<TimeSeriesKey<TDatasetKey>>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _bloomFactory = factory;
        _customBloomFactory = true;
        return this;
    }

    /// <summary>Configures a persisted custom Bloom implementation for new segments and reopening.</summary>
    /// <param name="factory">Creates a fresh, empty filter per segment. Storage owns the returned instances.</param>
    /// <param name="stateCodec">Borrowed, concurrency-safe codec with a stable identity/version; decodes fresh filters.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithBloomFilter(
        Func<TimeSeriesBloomFilterOptions, IBloomFilter<TimeSeriesKey<TDatasetKey>>> factory,
        IBloomFilterStateCodec<TimeSeriesKey<TDatasetKey>> stateCodec)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(stateCodec);
        WithBloomFilter(factory);
        return WithBloomFilterStateCodec(stateCodec);
    }

    /// <summary>Supplies the decoder for existing custom segments without changing the write factory.</summary>
    /// <param name="stateCodec">Borrowed, concurrency-safe codec matching persisted identity/version, returning fresh filters.</param>
    /// <returns>This builder.</returns>
    /// <remarks>Required even when new Bloom writes are disabled if existing segments use this codec.
    /// A missing or mismatched codec fails opening rather than excluding valid data as corrupt.</remarks>
    /// <exception cref="ArgumentNullException">The codec is null.</exception>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithBloomFilterStateCodec(
        IBloomFilterStateCodec<TimeSeriesKey<TDatasetKey>> stateCodec)
    {
        ArgumentNullException.ThrowIfNull(stateCodec);
        _bloomStateCodec = stateCodec;
        return this;
    }

    /// <summary>
    /// Enables WiscKey value-log dispatch. Valid only after <see cref="UseRowFormat"/> — columnar
    /// segments reject non-inline entries, so this fails fast otherwise.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current format is columnar.</exception>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> UseValueLog(Action<ValueLogOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (SegmentFormat != TimeSeriesSegmentFormat.Row)
        {
            throw new InvalidOperationException(
                "WiscKey dispatch requires UseRowFormat — columnar segments reject non-inline entries.");
        }

        var b = new ValueLogOptionsBuilder(ValueLogOptions.Default);
        configure(b);
        _valueLog = b.Build();
        _valueLogEnabled = true;
        return this;
    }

    /// <summary>Sets the quarantine behavior for corrupt row groups.</summary>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithQuarantineBehavior(QuarantineBehavior behavior)
    {
        if (!Enum.IsDefined(behavior))
        {
            throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Unsupported quarantine behavior.");
        }

        QuarantineBehavior = behavior;
        return this;
    }

    /// <summary>Sets the data-quality incident sink.</summary>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithDataQualityIncidentSink(IDataQualityIncidentSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        DataQualityIncidentSink = sink;
        return this;
    }

    /// <summary>Overrides the row-path SSTable block compression codec. No-op under columnar.</summary>
    public TimeSeriesStoreBuilder<TDatasetKey, TRecord> WithCompressionCodec(ICompressionCodec codec)
    {
        ArgumentNullException.ThrowIfNull(codec);
        _compressionCodec = codec;
        return this;
    }

    /// <summary>
    /// Translates <see cref="CompactionStrategy"/> into the matching Boutquin.Storage
    /// <see cref="ICompactionStrategy{TKey}"/> instance.
    /// </summary>
    public ICompactionStrategy<TimeSeriesKey<TDatasetKey>> ResolveCompactionStrategy()
        => CompactionStrategy switch
        {
            TimeSeriesCompactionStrategy.SizeTiered => new SizeTieredCompactionStrategy<TimeSeriesKey<TDatasetKey>>(),
            TimeSeriesCompactionStrategy.Leveled => new LeveledCompactionStrategy<TimeSeriesKey<TDatasetKey>>(),
            TimeSeriesCompactionStrategy.Full => new FullCompactionStrategy<TimeSeriesKey<TDatasetKey>>(DefaultFullCompactionThreshold),
            _ => throw new InvalidOperationException($"Unsupported time-series compaction strategy: {CompactionStrategy}."),
        };

    /// <summary>Builds a standalone filter using the configured factory, independently of physical-filter enablement.</summary>
    /// <returns>A fresh filter owned by the caller; dispose it after use.</returns>
    public IBloomFilter<TimeSeriesKey<TDatasetKey>> BuildBloomFilter() => _bloomFactory(_bloomOptions);

    /// <summary>
    /// The resolved on-disk segment folder after token substitution. Unless overridden, standalone
    /// builders use the absolute <c>.marketdata-timeseries</c> path beneath <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string ResolveSegmentFolder()
        => _segmentRoot.Replace("{record-type}", _recordTypeToken, StringComparison.Ordinal);

    /// <summary>
    /// Assembles and opens the live <see cref="LsmTimeSeriesStore{TDatasetKey, TRecord}"/> stack.
    /// </summary>
    /// <remarks>
    /// This legacy method is retained for binary compatibility but rejected for new source calls:
    /// its store-only result loses the disposal owner for the backing engine and WAL. Use
    /// <see cref="BuildStackAsync(CancellationToken)"/> and dispose the returned
    /// <see cref="TimeSeriesStack{TDatasetKey, TRecord}.Engine"/>. Dependency-injection registrations
    /// transfer that ownership to the service provider.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A constructed store ready to serve <c>AppendAsync</c>/<c>GetRangeAsync</c>.</returns>
    [Obsolete("BuildAsync discards the engine disposal owner. Use BuildStackAsync and dispose its Engine after use.", error: true)]
    public async Task<LsmTimeSeriesStore<TDatasetKey, TRecord>> BuildAsync(CancellationToken cancellationToken = default)
        => (await BuildStackAsync(cancellationToken).ConfigureAwait(false)).Store;

    /// <summary>
    /// Assembles and opens the live time-series stack, surfacing <b>both</b> the
    /// <see cref="LsmTimeSeriesStore{TDatasetKey, TRecord}"/> (the read/append API consumers use)
    /// and the inner <see cref="ILsmStorageEngine{TKey, TValue}"/> (whose
    /// <c>AcquireSnapshotAsync</c> the snapshot-isolated read path pins). The engine is the
    /// SAME instance the store reads through — registering both keeps the snapshot view coherent
    /// with the store's data.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The constructed store paired with its inner LSM engine.</returns>
    public async Task<TimeSeriesStack<TDatasetKey, TRecord>> BuildStackAsync(CancellationToken cancellationToken = default)
    {
        // Capture immutable settings before opening any files; later builder edits cannot
        // alter the factory used by this engine's background compaction.
        var bloomSettings = _bloomOptions;
        var bloomFactory = _bloomFactory;
        var bloomOptions = new SegmentBloomFilterOptions<TimeSeriesKey<TDatasetKey>>(
            bloomSettings.Enabled,
            bloomSettings.ExpectedItems,
            bloomSettings.FalsePositiveRate,
            _customBloomFactory
                ? (count, probability) => bloomFactory(bloomSettings with { ExpectedItems = count, FalsePositiveRate = probability })
                : null,
            _bloomStateCodec);
        var segmentFolder = ResolveSegmentFolder();

        // Directory creation is delegated to Boutquin.Storage's file-backed primitives — the WAL
        // ctor (creates walFolder, which creates segmentFolder as its parent) and
        // LsmStorageEngine.OpenAsync (creates segmentFolder) each create their own directory. The
        // DI package must not touch System.IO.Directory directly (FileSystemAccessTests rule).
        var walFolder = Path.Combine(segmentFolder, "wal");

        var wal = new WriteAheadLog<TimeSeriesKey<TDatasetKey>, TRecord>(walFolder, _walOptions);
        WalOwningLsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>? owningEngine = null;
        try
        {
            var compaction = ResolveCompactionStrategy();
            var sortSpec = ColumnSortSpec.DateAscending;

            ISegmentFormat<TimeSeriesKey<TDatasetKey>, TRecord> segmentFormat;
            TimeSeriesStoreOptions<TRecord> tsOptions;
            ValueLogOptions? engineValueLog;

            if (SegmentFormat == TimeSeriesSegmentFormat.Columnar)
            {
                var compressedFormat = new CompressedColumnarSegmentFormat<TRecord>(DefaultRowGroupMaxRows, TimeSpan.FromDays(365));
                var innerFormat = new QuarantineAwareColumnarSegmentFormat<TRecord>(
                    compressedFormat,
                    QuarantineBehavior,
                    DataQualityIncidentSink,
                    _quarantineTracker);
                segmentFormat = new ColumnarSegmentFormat<TDatasetKey, TRecord>(bloomOptions, innerFormat, sortSpec);
                // Storage 2.0 (Option-A relocation) moved ColumnarFormat off the format-agnostic
                // TimeSeriesStoreOptions<TRecord> onto the columnar-constrained derived options, so a
                // [StorageSerializable]-only record can configure a row store without the rehydration
                // constraint. TRecord is columnar-constrained at this branch (see the
                // CompressedColumnarSegmentFormat<TRecord> above), so the derived options type-check.
                tsOptions = new ColumnarTimeSeriesStoreOptions<TRecord>
                {
                    ColumnarFormat = innerFormat,
                };

                // Columnar segments reject non-inline entries — value-log is structurally off.
                engineValueLog = null;
            }
            else
            {
                segmentFormat = new SstSegmentFormat<TimeSeriesKey<TDatasetKey>, TRecord>(
                    bloomOptions, _compressionCodec,
                    SortedStringTable<TimeSeriesKey<TDatasetKey>, TRecord>.DefaultBlockSizeBytes,
                    SortedStringTable<TimeSeriesKey<TDatasetKey>, TRecord>.DefaultRestartInterval,
                    null, null);
                tsOptions = new TimeSeriesStoreOptions<TRecord>
                {
                    SegmentFormat = TimeSeriesSegmentFormat.Row,
                };

                // Enable value-log dispatch only when explicitly requested.
                engineValueLog = _valueLogEnabled ? _valueLog : null;
            }

            var lsmOptions = new LsmStorageEngineOptions<TimeSeriesKey<TDatasetKey>, TRecord>(
                DefaultMemTableCapacity,
                segmentFolder,
                SegmentPrefix: "ts",
                segmentFormat,
                wal,
                compaction,
                engineValueLog)
            {
                CorruptSegmentRecovery = _corruptSegmentRecovery,
                OnCorruptSegmentExcluded = _onCorruptSegmentExcluded,
            };

            var innerEngine = await LsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>
                .OpenAsync(lsmOptions, cancellationToken).ConfigureAwait(false);
            owningEngine = new WalOwningLsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>(innerEngine, wal);

            var tsEngine = new LsmTimeSeriesStorageEngine<TDatasetKey, TRecord>(owningEngine, tsOptions);

            var coverage = _coverageIndex ?? new InMemoryDatasetCoverageIndex<TDatasetKey>();

            var store = new LsmTimeSeriesStore<TDatasetKey, TRecord>(tsEngine, coverage);

            // The ownership wrapper is the one ILsmStorageEngine identity exposed to the store,
            // stack, and registry. It delegates snapshots and every data operation to innerEngine,
            // while coupling disposal to the caller-owned WAL that MarketData allocated.
            return new TimeSeriesStack<TDatasetKey, TRecord>(store, owningEngine);
        }
        catch (Exception ex)
        {
            WalOwningLsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>
                .DisposeAfterBuildFailure(owningEngine, wal, ex);
            throw;
        }
    }

    internal ITimeSeriesStore<TDatasetKey, TRecord> WrapStoreForQuarantineTracking(
        ITimeSeriesStore<TDatasetKey, TRecord> store)
        => SegmentFormat == TimeSeriesSegmentFormat.Columnar
            ? new QuarantineTrackingTimeSeriesStore<TDatasetKey, TRecord>(store, _quarantineTracker)
            : store;
}

/// <summary>
/// The constructed time-series stack: the consumer-facing
/// <see cref="LsmTimeSeriesStore{TDatasetKey, TRecord}"/> paired with the inner
/// <see cref="ILsmStorageEngine{TKey, TValue}"/> that backs both its reads and the
/// snapshot-isolated read path, and owns the MarketData-allocated WAL lifetime.
/// </summary>
/// <typeparam name="TDatasetKey">Dataset partition key type.</typeparam>
/// <typeparam name="TRecord">Dated, columnar-rehydratable record type.</typeparam>
/// <param name="Store">The read/append store consumers use.</param>
/// <param name="Engine">The owning LSM engine boundary whose <c>AcquireSnapshotAsync</c> the snapshot path pins.</param>
public readonly record struct TimeSeriesStack<TDatasetKey, TRecord>(
    LsmTimeSeriesStore<TDatasetKey, TRecord> Store,
    ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> Engine)
    where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    where TRecord : ISerializable<TRecord>, IHasDate, new();

/// <summary>
/// Mutable builder over <see cref="WriteAheadLogOptions"/> — Storage's options record is
/// init-only, so this exposes settable knobs for the <c>WithWriteAheadLogOptions</c> callback.
/// </summary>
public sealed class WriteAheadLogOptionsBuilder
{
    /// <summary>Creates a builder seeded from <paramref name="seed"/>.</summary>
    public WriteAheadLogOptionsBuilder(WriteAheadLogOptions seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        GroupCommitBatchSize = seed.GroupCommitBatchSize;
        GroupCommitLatencyMs = seed.GroupCommitLatencyMs;
        SegmentMaxBytes = seed.SegmentMaxBytes;
    }

    /// <summary>Number of appends coalesced into one fsync.</summary>
    public int GroupCommitBatchSize { get; set; }

    /// <summary>Latency cap (ms) before forcing an fsync on a partial batch.</summary>
    public int GroupCommitLatencyMs { get; set; }

    /// <summary>WAL segment rotation threshold in bytes.</summary>
    public long SegmentMaxBytes { get; set; }

    /// <summary>Materialises the configured options.</summary>
    public WriteAheadLogOptions Build() => new()
    {
        GroupCommitBatchSize = GroupCommitBatchSize,
        GroupCommitLatencyMs = GroupCommitLatencyMs,
        SegmentMaxBytes = SegmentMaxBytes,
    };
}

/// <summary>
/// Mutable builder over <see cref="ValueLogOptions"/> — Storage's options record is init-only,
/// so this exposes the threshold knob for the <c>UseValueLog</c> callback.
/// </summary>
public sealed class ValueLogOptionsBuilder
{
    /// <summary>Creates a builder seeded from <paramref name="seed"/>.</summary>
    public ValueLogOptionsBuilder(ValueLogOptions seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ValueLogThresholdBytes = seed.ValueLogThresholdBytes;
    }

    /// <summary>Values strictly longer than this (bytes) are written to the value log.</summary>
    public int ValueLogThresholdBytes { get; set; }

    /// <summary>Materialises the configured options.</summary>
    public ValueLogOptions Build() => ValueLogOptions.Default with { ValueLogThresholdBytes = ValueLogThresholdBytes };
}
