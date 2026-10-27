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
using Boutquin.Storage.Domain.ValueObjects.ValueLog;
using Boutquin.Storage.Infrastructure.WriteAheadLog;

namespace Boutquin.MarketData.Orchestration.Pipeline;

/// <summary>
/// Configurable options controlling the data pipeline's L1 cache lifetime and partition
/// failure-handling strategy.
/// </summary>
/// <remarks>
/// <para>
/// Binding: supply via <c>IOptions&lt;DataPipelineOptions&gt;</c> through the standard .NET
/// options pattern. Defaults are tuned for end-of-day workflows (1-hour L1 TTL, continue on
/// partition failure) where occasional gaps are preferable to an entire batch failing.
/// </para>
/// <para>
/// Intraday tuning: shorten <see cref="L1CacheTtl"/> when the pipeline is fed by a scheduler
/// that polls faster than the cache window — a 1-hour TTL will serve stale bars to a 15-minute
/// poller. Consumers that stop after the first failed partition should set
/// <see cref="ContinueOnPartitionFailure"/> to <see langword="false"/> and inspect the returned
/// <c>PARTITION_FAILED</c> issue before using the successful prefix.
/// </para>
/// </remarks>
public sealed class DataPipelineOptions
{
    private TimeSpan? _timeSeriesStalenessMaxAge;

    /// <summary>
    /// How long a completed envelope remains in the L1 in-memory cache before the next request
    /// for the same cache key falls through to L2 or the live adapter chain. Applies per cache
    /// entry, starting from the moment the entry is written.
    /// </summary>
    /// <remarks>
    /// Default: 1 hour — fits daily bar workflows that re-run once per day with the same inputs.
    /// Lower this for intraday polling; raise it for historical backfills where the underlying
    /// data is known to be immutable.
    /// </remarks>
    public TimeSpan L1CacheTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// When <see langword="true"/> (default), a single partition failure is recorded as a
    /// <c>PARTITION_FAILED</c> data issue and the pipeline continues with the remaining
    /// partitions, returning a partial result. When <see langword="false"/>, the pipeline stops
    /// at the first partition failure and returns what it has gathered so far.
    /// </summary>
    /// <remarks>
    /// Set to <see langword="false"/> when dispatch must stop at the first failure; the returned
    /// envelope still contains the successful prefix and a <c>PARTITION_FAILED</c> issue. Leave
    /// <see langword="true"/> for analytics backfills where "most symbols" is preferable to "no symbols"
    /// on a transient outage.
    /// </remarks>
    public bool ContinueOnPartitionFailure { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, <c>DataPipeline.ExecuteAsync</c> routes cache reads and
    /// gap-fill writes through the time-series store (an <c>ITimeSeriesStore&lt;TDatasetKey, TRecord&gt;</c>
    /// resolved via <c>ITimeSeriesStoreRegistry</c>). When <see langword="false"/> (default),
    /// the pipeline retains its legacy L2 snapshot-store behaviour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Preview feature flag. Defaults to <see langword="false"/> on first release so consumers
    /// opt in after validating their adapters against the new flow; the default flips to
    /// <see langword="true"/> one release after first GA, and the flag is removed one release
    /// after that.
    /// </para>
    /// <para>
    /// <b>Storage format versioning — action required for preview-flag adopters.</b> Any
    /// deployment that populated the time-series store while running Boutquin.Storage 1.x
    /// (the pre-2.0 wire format) <b>must delete the stored segments and re-ingest before
    /// toggling this option on under Storage 2.0</b>. The Storage 2.0 reader surfaces
    /// <c>UnsupportedFormatVersionException</c> on first contact with 1.x bytes; the pipeline
    /// re-throws unwrapped so the caller sees the exception at the <c>ExecuteAsync</c> call site.
    /// See <see href="docs/time-series-cache.md#storage-format-versioning">Storage format
    /// versioning</see> for the delete-and-re-ingest procedure.
    /// </para>
    /// <para>Has no effect when the DI container has not registered a time-series store
    /// via <c>IMutableTimeSeriesStoreRegistry.Register</c> — the pipeline falls back to the
    /// legacy path in that case with a one-time warning.</para>
    /// </remarks>
    public bool UseTimeSeriesStore { get; set; }

    /// <summary>
    /// When <see langword="true"/>, <c>AddMarketDataKernel</c> registers the normalizer shipped
    /// by <c>Boutquin.MarketData.Normalization</c> for every canonical record type that has one,
    /// so <c>DataPipeline</c> resolves a live <c>INormalizer&lt;TRecord, TRecord&gt;</c> and the
    /// normalization stage runs. When <see langword="false"/> (default), the kernel registers
    /// none and records pass through un-normalized unless the host registers its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pipeline context:</b> normalization is the <c>adapter → normalize → envelope</c> stage.
    /// The pipeline resolves the normalizer optionally and tolerates its absence, so a host that
    /// leaves this <see langword="false"/> and registers nothing gets raw adapter values — valid,
    /// but un-clamped and un-rounded. The kernel emits an <c>Information</c> log naming the
    /// registered record-type count when this is <see langword="true"/>, and a <c>Debug</c>
    /// pass-through notice when it is <see langword="false"/>, so neither path is silent. The
    /// notice is emitted once per built provider, on first <c>IDataPipeline</c> resolution
    /// (composition itself runs before any <c>IServiceProvider</c> exists, so no logger factory is
    /// resolvable there).
    /// </para>
    /// <para>
    /// <b>Override and opt-out:</b> kernel registrations use <c>TryAddSingleton</c>, so a host
    /// registration for a record type — made before or after <c>AddMarketDataKernel</c> — is the
    /// instance the container resolves. To suppress one record type, call
    /// <c>services.RemoveAll&lt;INormalizer&lt;T, T&gt;&gt;()</c> after the kernel call; there is
    /// no bespoke exclusion API.
    /// </para>
    /// <para>
    /// <b>Cache coherence — action required when enabling over an existing cache.</b>
    /// Normalization runs <b>before</b> the L2 write-through and the time-series append, so
    /// turning this on over an already-populated cache or store directory mixes previously
    /// stored raw records with newly stored normalized ones under the same keys. Enable it from
    /// the start of a cache directory's life, or clear the L2 snapshot directory and time-series
    /// segments when turning it on for an existing host.
    /// </para>
    /// <para>
    /// <b>Lifetime:</b> the registrations are singletons — valid because every shipped normalizer
    /// is stateless across <c>Normalize</c> calls. A normalizer carrying mutable per-call state
    /// cannot join the default set as a singleton.
    /// </para>
    /// <para>
    /// Read at resolution time, off the host's built provider — both by each normalizer's
    /// resolution-time factory and by the notice above. Never read while the collection is
    /// being composed, and no value is held onto for later reuse; <c>DataPipeline</c> never
    /// reads it directly. Because every channel is read at resolution, a value supplied
    /// through <c>Configure</c> (before or after <c>AddMarketDataKernel</c>),
    /// <c>PostConfigure</c>, or <c>IConfiguration</c> section binding is honored.
    /// <c>InstrumentMetadata</c> has no shipped normalizer and therefore resolves none even
    /// when this is <see langword="true"/>.
    /// </para>
    /// </remarks>
    public bool RegisterDefaultNormalizers { get; set; }

    /// <summary>
    /// Selects the on-disk segment format for time-series store engines constructed by DI
    /// default wiring. Defaults to <see cref="TimeSeriesSegmentFormat.Columnar"/> —
    /// MarketData's read pattern (many rows, few tickers, long date ranges, heavy
    /// <c>decimal</c>/<c>long</c> payload) is textbook columnar territory (DDIA 2e Ch. 4).
    /// </summary>
    /// <remarks>
    /// <para>Per-record-type overrides are configured through the
    /// <c>configure</c> action on <c>AddTimeSeriesStore&lt;TDatasetKey, TRecord&gt;</c>
    /// in the DI builder (see
    /// <see href="docs/time-series-cache.md#choosing-the-segment-format">Choosing the segment
    /// format</see>); consumers whose specific record shape benchmarks faster under
    /// <see cref="TimeSeriesSegmentFormat.Row"/> opt out one record type at a time. That path
    /// is a safety valve — not an expected code path on any shipped MarketData record.</para>
    /// <para>Only read when <see cref="UseTimeSeriesStore"/> is <see langword="true"/>;
    /// otherwise ignored.</para>
    /// </remarks>
    public TimeSeriesSegmentFormat TimeSeriesSegmentFormat { get; set; } = TimeSeriesSegmentFormat.Columnar;

    /// <summary>
    /// On-disk root for time-series segment files. The interpolation token <c>{record-type}</c>
    /// is replaced per-store by the DI wiring so each record type gets its own sub-directory.
    /// Defaults to <c>"$storage-root/timeseries/{record-type}"</c>. Only read when
    /// <see cref="UseTimeSeriesStore"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// The WAL for each store lives in a sibling <c>wal/</c> sub-directory with rolling
    /// <c>wal_NNNNNN.log</c> segments. Per-record overrides are available via the
    /// <c>WithSegmentRoot(string)</c> method on the store builder (see
    /// <see href="docs/time-series-cache.md#choosing-the-compaction-strategy">Choosing the
    /// compaction strategy</see> for the relationship between segment root and compaction
    /// behaviour).
    /// </remarks>
    public string TimeSeriesSegmentRootPath { get; set; } = "$storage-root/timeseries/{record-type}";

    /// <summary>
    /// LSM compaction schedule applied to every DI-constructed time-series store. Defaults to
    /// <see cref="TimeSeriesCompactionStrategy.SizeTiered"/> — matches MarketData's
    /// append-mostly workload (DDIA 2e Ch. 3 p. 124, Cassandra-style size-tier bucketing).
    /// Only read when <see cref="UseTimeSeriesStore"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TimeSeriesCompactionStrategy.SizeTiered"/> groups level-0 segments into
    /// size-tier buckets and merges the smallest bucket first — ideal for the nightly
    /// append burst that populates MarketData's date-ascending time series. Use
    /// <see cref="TimeSeriesCompactionStrategy.Leveled"/> when read amplification is the
    /// primary concern (LevelDB-style schedule; more write cost, better read locality).
    /// <see cref="TimeSeriesCompactionStrategy.Full"/> merges all segments into one in a
    /// single pass — useful only for tiny test datasets. See
    /// <see href="docs/time-series-cache.md#choosing-the-compaction-strategy">Choosing the
    /// compaction strategy</see> for a workload-by-workload decision matrix.
    /// </remarks>
    public TimeSeriesCompactionStrategy TimeSeriesCompactionStrategy { get; set; }
        = TimeSeriesCompactionStrategy.SizeTiered;

    /// <summary>
    /// Group-commit write-ahead-log tuning for time-series store engines. Defaults match the
    /// Storage 2.0 group-commit WAL contract (256-append batch, 5 ms latency cap, 64 MiB
    /// segment rotation; Hellerstein 2007, section 4.1). The removed per-append <c>FsyncOnEveryAppend</c>
    /// toggle is replaced by setting <c>GroupCommitBatchSize = 1</c> for near-per-append fsync.
    /// Only read when <see cref="UseTimeSeriesStore"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The group-commit mode coalesces up to <c>GroupCommitBatchSize</c> in-flight
    /// <c>AppendAsync</c> calls into a single <c>fsync</c>, bounded by the
    /// <c>GroupCommitLatencyMs</c> timeout. A nightly gap-fill loop appending 100 rows for
    /// one ticker typically completes under 5 fsyncs at the default settings — the entire
    /// batch lands within one group-commit window.
    /// </para>
    /// <para>
    /// WAL segments rotate when their on-disk size reaches <c>SegmentMaxBytes</c> (64 MiB
    /// default). Old segments are archived in the per-store <c>wal/</c> directory and pruned
    /// by the LSM engine after compaction confirms the covered data is durable in the SSTable
    /// layer.
    /// </para>
    /// </remarks>
    public WriteAheadLogOptions TimeSeriesWriteAheadLog { get; set; } = new()
    {
        GroupCommitBatchSize = 256,
        GroupCommitLatencyMs = 5,
        SegmentMaxBytes = 64L * 1024L * 1024L,
    };

    /// <summary>
    /// Sizes physical point-lookup Bloom filters in new row and columnar segments.
    /// Defaults to 131,072 expected items and a 1% false-positive target.
    /// </summary>
    /// <remarks>
    /// Settings apply to flush and compaction output. Reopening uses persisted state, so changing
    /// settings or disabling new Bloom writes does not discard existing filters. Configuration is
    /// validated during kernel composition regardless of <see cref="UseTimeSeriesStore"/>.
    /// </remarks>
    public TimeSeriesBloomFilterOptions TimeSeriesBloomFilter { get; set; } = new();

    /// <summary>
    /// WiscKey value-log options for row-format time-series engines. The default
    /// <see langword="null"/> disables value-log dispatch. Only read when
    /// <see cref="UseTimeSeriesStore"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WiscKey key-value separation is disabled by default because MarketData records are
    /// all small (a serialised <c>Bar</c> is roughly 60 bytes) and because the default
    /// <see cref="TimeSeriesSegmentFormat.Columnar"/> segment format rejects non-inline
    /// entries at the Storage layer. A <see langword="null"/> option keeps value-log
    /// dispatch disabled at the Storage engine boundary.
    /// </para>
    /// <para>
    /// A record type that genuinely benefits from WiscKey — for example, a hypothetical
    /// <c>CorrelationMatrixSnapshot</c> whose serialised payload exceeds the 1024-byte
    /// default threshold — opts out of columnar <b>and</b> enables a finite threshold in a
    /// single builder call via <c>UseRowFormat().UseValueLog(opts)</c>. The builder rejects
    /// a half-configuration (value-log without row format) at construction time. See
    /// <see href="docs/time-series-cache.md#wisckey-reconciliation">WiscKey reconciliation</see>
    /// for the rationale and the per-record opt-in pattern.
    /// </para>
    /// </remarks>
    public ValueLogOptions? TimeSeriesValueLog { get; set; }

    /// <summary>
    /// Governs the pipeline's reaction when a columnar row group is quarantined on read.
    /// Defaults to <see cref="QuarantineBehavior.TreatAsGap"/> — the self-healing
    /// default that re-fetches the affected window via gap fill. Only read when
    /// <see cref="UseTimeSeriesStore"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// <see cref="QuarantineBehavior.TreatAsGap"/> (default): the caller's request window is
    /// re-fetched from the adapter (bounded to that request); fresh records are appended and the returned envelope
    /// carries <c>RetrievalMode = Partial</c> with a quarantine flag in provenance. A
    /// <see cref="DataQualityIncidentSink"/> (if configured) receives a
    /// <c>DataQualityIncident</c> with the row-group index and CRC mismatch detail.
    /// </description></item>
    /// <item><description>
    /// <see cref="QuarantineBehavior.SkipRowGroup"/>: intact surrounding row groups are
    /// returned and the quarantine is logged; no recovery fetch is scheduled.
    /// </description></item>
    /// <item><description>
    /// <see cref="QuarantineBehavior.FailQuery"/>: the exception surfaced by the Storage
    /// read path is propagated to the <c>ExecuteAsync</c> caller.
    /// </description></item>
    /// </list>
    /// See <see href="docs/time-series-cache.md#quarantine-and-data-quality-incidents">Quarantine
    /// and data-quality incidents</see> for the full policy matrix and incident-sink contract.
    /// </remarks>
    public QuarantineBehavior QuarantineBehavior { get; set; } = QuarantineBehavior.TreatAsGap;

    /// <summary>
    /// Maximum age of the newest stored record, measured against the injected UTC clock,
    /// before the time-series pipeline re-fetches the inclusive trailing span from the adapter.
    /// The newest date is probed via <c>GetTailAsync(dataset, 1)</c>.
    /// Defaults to <see langword="null"/> — no staleness extension; the store's coverage
    /// alone drives gaps. Only read when <see cref="UseTimeSeriesStore"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// When the UTC clock is more than this age past the newest stored date and
    /// <c>request.To</c> is on or after that date, the inclusive gap is
    /// <c>max(newestDate, request.From) .. request.To</c>. Including the newest date permits
    /// correction of its last observation even when the request ends on that date.
    /// The newest-record probe is an O(tail-row-group) read that avoids calendar-window guessing.
    /// Values must be non-negative; assigning a negative duration throws an
    /// <see cref="ArgumentOutOfRangeException"/>.
    /// </remarks>
    public TimeSpan? TimeSeriesStalenessMaxAge
    {
        get => _timeSeriesStalenessMaxAge;
        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Staleness maximum age cannot be negative.");
            }

            _timeSeriesStalenessMaxAge = value;
        }
    }

    /// <summary>
    /// Maximum number of logical-request entries retained by each in-memory time-series recovery
    /// accelerator. Defaults to 1,024 entries; durable recovery metadata remains authoritative
    /// after an entry is evicted.
    /// </summary>
    /// <remarks>
    /// The selected-provider and attempted-range accelerators share this bound so their volatile
    /// footprint stays aligned with the L1 cache tier. Use a larger value only when a process
    /// routinely serves more distinct datasets within one L1 lifetime.
    /// </remarks>
    public int TimeSeriesRecoveryIndexCapacity { get; set; } = 1_024;

    /// <summary>
    /// How long an in-memory time-series recovery accelerator entry remains eligible for reuse.
    /// Defaults to 1 hour, matching <see cref="L1CacheTtl"/>; durable recovery metadata survives
    /// this expiry and process restart.
    /// </summary>
    public TimeSpan TimeSeriesRecoveryIndexTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Optional sink for quarantine and ingestion-anomaly incidents. Defaults to
    /// <see langword="null"/>, in which case the pipeline emits incidents via
    /// <c>ILogger</c>. Only read when <see cref="UseTimeSeriesStore"/> is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>DataQualityIncident</c> is emit-and-forget: the pipeline constructs one per
    /// incident, hands it to the sink (or logger) synchronously within <c>ExecuteAsync</c>,
    /// and retains no reference. Retention and archival of emitted incidents is wholly the
    /// sink's concern — the default <c>ILogger</c> sink inherits the host's log-retention
    /// policy; the pipeline holds no incident buffer and has no replay surface.
    /// </para>
    /// <para>
    /// See <see href="docs/time-series-cache.md#quarantine-and-data-quality-incidents">Quarantine
    /// and data-quality incidents</see> for the incident contract and a worked example
    /// implementing a persistent sink.
    /// </para>
    /// </remarks>
    public IDataQualityIncidentSink? DataQualityIncidentSink { get; set; }
}
