# Time-Series Store Cache Tier

`DataPipeline` can route cache reads and gap-fill writes through a columnar LSM
time-series store instead of the legacy L2 snapshot store. The feature ships
behind `DataPipelineOptions.UseTimeSeriesStore` (default **`false`**) — with the
flag off, the pipeline runs its existing L2 snapshot behaviour unchanged. This
page documents what ships when the flag is on: the gap-fill flow, snapshot-isolated
reads, the storage-format wire break, segment-format and compaction choices, the
quarantine policies, and why WiscKey key-value separation is disabled by
default.

## How the cache tier works

With `UseTimeSeriesStore = true`, `DataPipeline.ExecuteAsync` replaces the L2
exact-key check with a coverage-aware read against a per-record-type
`ITimeSeriesStore<TDatasetKey, TRecord>`:

1. **L1 check.** The in-memory TTL cache is checked first, unchanged.
2. **Dataset key.** The request is projected into its typed `DatasetKey`
   (`PriceBarDatasetKey`, `FxRateDatasetKey`, …) via the registered
   `IDatasetKeyBuilder<TRequest, TDatasetKey>`.
3. **Coverage + gaps.** The store is asked for its coverage and for the gaps
   between that coverage and the requested date range.
4. **Full subsumption is free.** If there are no gaps, the range is served
   directly from the store — no adapter dispatch. The response is stamped
   `RetrievalMode.Cache`.
5. **Gap fill.** Each gap is a separate adapter dispatch. Adapter rate-limiting
   and fallback ordering are unchanged. Records returned by the adapter are
   still archived to the raw document store for audit, then appended to the
   time-series store — the LSM engine dedupes on `(DatasetKey, Date)` with
   last-writer-wins semantics, so a partially-overlapping re-dispatch never
   doubles a record.
6. **Merged read.** After gap fill, the full requested range is read back from
   the store (now including the freshly appended records) and returned.
7. **Provenance.** A response served entirely from the store is `Cache`; a
   response that required at least one adapter dispatch is `Live` (the whole
   range was a gap) or `Partial` (some of the range was already covered).

The practical effect: the first request for a ticker or series pays one full
adapter fetch and populates the store; every subsequent request that falls
within already-covered dates is served with zero adapter calls, and only the
genuinely new dates (a nightly extend-by-one-day, a wider historical window)
trigger a fetch.

**This tier is off by default.** `DataPipelineOptions.UseTimeSeriesStore`
defaults to `false`, so existing consumers keep the legacy L2 snapshot-store
behaviour until they explicitly opt in. See
[Storage format versioning](#storage-format-versioning) before flipping the
flag on an existing data directory.

### Restart recovery and bounded indexes

For single-row series, the pipeline persists the providers that contributed to a
logical request and the ranges for which each provider returned complete coverage.
The metadata uses the configured checkpoint store, alongside the data stores. A
rebuilt host can recover the provider-specific keys and serve source-confirmed
empty dates without a live adapter. Preserve the checkpoint directory together
with the time-series directory when moving a cache.

When fallback changes the selected provider, reads reconcile all contributing
physical keys. Keys are ordered canonically; the first key wins a duplicate date,
so a restart preserves the same result. A partial response does not confirm an
entire attempted range and unresolved dates remain eligible for another fetch.

`TimeSeriesRecoveryIndexCapacity` (default 1,024) and
`TimeSeriesRecoveryIndexTtl` (default one hour) bound the in-memory provider and
attempted-range indexes. Both must be positive. They are accelerators: expiry or
capacity eviction does not erase the single-row recovery metadata on disk.

Multi-row series also persist source-confirmed empty spans for each already-known
discriminant key. A factor or tenor observed on an earlier date can therefore stay
absent on a later date across restart without an unnecessary live request. Only a
complete source response establishes absence; a partial response leaves the
missing discriminant eligible for another fetch. This does not infer undiscovered
keys from an entirely empty initial response.

The pipeline uses the host's registered request partitioner in both cache modes.
Without a custom partitioner, multi-symbol requests retain one-symbol partitions.
`ContinueOnPartitionFailure` controls whether later partitions are attempted;
cancellation propagates instead of becoming a partition issue.

### Per-row-discriminant fan-out for multi-row-per-date record types

With `UseTimeSeriesStore = true`, record types that return **multiple rows for a
single date** — a yield curve (many tenors as of one date), an option chain (many
strikes/expiries/types), a factor set (many factor names) — fan out to **one series
per row** instead of collapsing to a single row. On the gap-fill append path, each
fetched record's own discriminant (not blanked) is used to build a fully-populated
`DatasetKey`, and each co-dated row is appended under its own key — so co-dated rows
never collide on `(DatasetKey, Date)`.

| Record | Multi-row axis | Discriminant | Where it comes from |
|---|---|---|---|
| `YieldCurveQuote` | tenors on one curve date | `Tenor` | record body |
| `FactorObservation` | factor names on one date | `FactorName` | record body |
| `OptionQuote` | strikes × expiries × types on one quote date | `Expiry` / `Strike` / `Type` | keyed-row fetch envelope (below) |

For `YieldCurveQuote` and `FactorObservation` the discriminant already lives on the
fetched record's body, so the fan-out reads it directly. `OptionQuote` is different:
its body carries only the per-date quote — `Strike`/`Expiry`/`Type` live in
`OptionQuoteDatasetKey`, not on the record — so there is no body field to fan out
on. Instead, the option-chain adapter emits a **non-persisted keyed-row envelope**,
`OptionChainRow(OptionContract Contract, OptionQuote Quote)`, pairing each row's
contract identity with its slim quote body, and registers as
`IDataSourceAdapter<OptionChainRequest, OptionChainRow>`. The fan-out projects each
`OptionChainRow` to its own fully-populated
`OptionQuoteDatasetKey(Provider, Underlying, Expiry, Strike, Type)` and appends only
the slim `OptionQuote` — the envelope itself is never written to the store.

#### The discriminant is canonicalised in the key, always

For the two body-discriminant types the projected key column is **canonicalised by
the key type itself** — `FactorObservationDatasetKey.CanonicalFactorName` (trim, then
the academic spelling for a known factor, else uppercase) and
`YieldCurveQuoteDatasetKey.CanonicalTenor` (trim + uppercase) — on **every** append,
whether or not a normalizer is registered. The default normalizers rewrite those same
body columns through the same two functions, so the persisted key is *invariant under
normalization*: turning `RegisterDefaultNormalizers` on over a populated store cannot
split one logical factor or tenor into a raw-era key and a normalized-era key. (Those
two keys would both fall inside the `(prefix, *)` scan and both satisfy the membership
filter, which deliberately does not filter the discriminant, so the cross-key read
would merge two parallel series — permanently, because the raw-era key is never
re-fetched.) The [cache-coherence rule](normalization.md) — a read spanning the flip
can blend raw and normalized *values* under one key until the segments are cleared —
still applies; it is a same-key value blend, not a key fork.

The canonicalisation applies to the key column only; the record body is written
unchanged.

#### Migrating a store written before the canonicalisation

Builds that predate the canonicalisation projected the key column straight off the
record body, so **this is an on-disk key-encoding change** for any series whose
discriminant was not already canonical. Whether your store is affected depends on
what the source publishes, not on any flag:

| Source | Discriminant it publishes | Key encoding across the upgrade |
|---|---|---|
| `UsTreasuryYieldAdapter` | a fixed tenor map, `"1M"`…`"30Y"` | unchanged — already canonical |
| `BankOfCanadaYieldCurveAdapter` | the last dot-segment of the Valet series name (`BD.CDN.ZERO.3M` → `"3M"`) | unchanged for every group name this repo ships or has observed — the group name is an operator-configurable, source-controlled string (`BankOfCanadaOptions.GroupName`), not an enumerable set, so this is verified on the shipped default and fixtures rather than guaranteed for every possible Valet group; a differently-cased tenor segment would trip the fail-fast guard below, not silently corrupt |
| `FamaFrenchFactorAdapter` | the **raw CSV header cells** of whichever archive the request named (`FactorDatasetId` accepts any non-blank dataset name) | **changes** for any header `h` where `FactorObservationDatasetKey.CanonicalFactorName(h) != h` — either a name outside the eight canonical spellings, trimmed and uppercased (`"ST_Rev"` → `"ST_REV"`, `"Lo 10"` → `"LO 10"`, `"SMALL LoBM"` → `"SMALL LOBM"`), or a differently-cased variant of a known name re-cased to its mixed-case canonical spelling (`"MOM"` → `"Mom"`, `"mkt-rf"` → `"Mkt-RF"`) |

So a store populated by an earlier build against a factor archive with such headers
holds keys this build no longer writes. The two keys are **not** interchangeable: the
legacy key is never re-fetched, the canonical key reports the whole history as a gap
and re-fetches it, and the discriminant-blind membership filter merges both — which
would return every shared date twice.

**The store therefore fails fast rather than re-keying silently.** The first fan-out
read that reaches a dataset key whose discriminant is not in the current encoding
raises `Boutquin.MarketData.Abstractions.Exceptions.MarketDataStorageException`,
naming the stored spelling, the spelling this build writes, and this section. The
check is exact: canonicalisation is idempotent, so every key written by this build
satisfies `Canonical(k) == k`, and a stored key that fails that equality provably
predates the change.

**Action required:** delete the stored time-series segments for the affected record
type and re-ingest — the same remedy, and for the same reason, as the
[storage format versioning](#storage-format-versioning) break below. The time-series
tier is a rebuildable cache, so delete-and-re-ingest is safe and is the only supported
path. A read-path alias onto the raw spelling is deliberately *not* offered: it leaves
the duplicate-count hazard live, because any later re-dispatch over the historical
range (a stale tail, a widened request, a cleared attempted-range index) appends
canonical-key rows for dates the legacy key already holds.

On the **read** path, a request spanning the whole multi-row set (the full yield
curve as of a date, the full factor set as of a date, the full option chain as of a
date) merges across the N discriminant keys via a `(prefix, *)` cross-key scan and
returns them in dataset-key sort order.

Coverage and gap detection are per-discriminant-key: a gap in one tenor, factor, or
contract does not force a re-fetch of the covered ones. A discriminant that is
historically known (has at least one stored row) but permanently absent at the source
over part of the requested range — a rolled-off or expired option contract, a
discontinued futures month — is dispatched at most once for that hole, not on every
read: the range dispatched for each discriminant is remembered, and a sub-span the
source came back empty on is clamped out of future gap unions rather than re-fetched
forever. A genuine per-tenor gap that the source *can* fill still heals normally,
because only spans that remain empty after a fill are recorded as attempted.

**Idempotency.** Re-running the fan-out over an unchanged N-row set leaves the read
returning the same N rows, not 2N — each discriminant key dedups last-writer-wins
independently. If a single fetch itself contains a duplicate discriminant (e.g. two
rows for the same tenor), the duplicate collapses last-writer-wins within that fetch
and a `DataQualityIncident` is logged naming the collapsed key, so the collision is
observable rather than silent.

Single-row-per-date types — equity/ETF bars (`Bar`), economic series
(`ScalarObservation`), and per-pair FX rates (`FxRate`) — are unaffected, as are
multi-symbol and multi-pair requests; they continue to use a single `DatasetKey` per
request. `FuturesSettlement` uses `FuturesSettlementFanOut` to persist each
`ContractMonth` under its own `(Provider, ContractCode, ContractMonth)` key. The
default registration includes its fan-out catalogue entry and row-store cross-key
scan, so cold fetches, cache hits, and restart recovery retain co-dated maturities.

#### Futures contract-month migration

Earlier builds stored all futures maturities under a blank month key, which could
overwrite same-day contracts. Such a key now raises `MarketDataStorageException`
before the pipeline merges or appends records for that product. Use a fresh
time-series store and recovery-checkpoint directory and re-ingest the futures data
from its source. The old cache cannot reconstruct maturities that were overwritten;
there is no silent re-keying or legacy-key fallback. Existing files are not deleted
automatically. The request-level key builder still returns a blank month solely
for request identity; persisted rows use the fan-out's full month key.

As with the rest of this tier, this is all behind
`DataPipelineOptions.UseTimeSeriesStore` (default `false`); no shipped consumer is
affected until they opt in.

## Snapshot-isolated reads

Long-running consumers — backtests, curve recalibrations, batched factor-model
runs — often need repeatable reads while a concurrent nightly refresh appends
data. The handle's first read for each record type captures that type's engine
watermark. It is not a cross-record-type atomic snapshot, and creating the
handle does not capture engine state:

```csharp
await using var snap = await pipeline.AcquireReadSnapshotAsync(ct);

var range = new DateRange(from, to);
var spy = await snap.ExecuteAsync<Bar>(new PriceHistoryRequest([new Symbol("SPY")], range), ct);
var agg = await snap.ExecuteAsync<Bar>(new PriceHistoryRequest([new Symbol("AGG")], range), ct);
// Both Bar responses use the watermark captured by the first Bar read.
// A first read of another record type captures its own, potentially later watermark.
```

Semantics:

- The first `ExecuteAsync` call for a given record type on the handle acquires
  an underlying engine snapshot and caches it; later calls for the same record
  type reuse that snapshot.
- Records written before a type's first read may be visible even if appended
  after handle creation. Later appends are invisible to that type's captured
  view; different record types can observe different capture times.
- Only columnar-rehydratable types with a registered snapshot binding are
  supported. Row-only `DividendPayment` and `FuturesSettlement` cannot be read
  through the handle. A row-format store for a columnar-rehydratable type can
  still participate; storage format alone does not determine eligibility.
- **A gap inside a snapshot-bound read fails fast** with `SnapshotGapException`
  rather than silently dispatching an adapter. Fetching a gap from inside a
  snapshot would both violate the snapshot's stability guarantee and leave the
  newly-appended records invisible to the same snapshot (since they land after
  acquisition) — a confusing "why didn't that come back" experience. Callers
  who want "read from a snapshot, fall back to live" compose a snapshot read
  and a separate live `ExecuteAsync` call explicitly.
- Disposal (`await using`) releases the underlying engine's segment pins in
  acquisition-inverse order. A live snapshot bounds the LSM engine's ability to
  reclaim retired segments during compaction for as long as it stays open —
  always dispose promptly rather than holding a snapshot for the lifetime of a
  long process.
- L1 is bypassed entirely inside a snapshot handle, since L1's TTL semantics
  are incompatible with point-in-time isolation.

## Storage format versioning

The time-series store consumes [Boutquin.Storage](https://github.com/boutquin/Boutquin.Storage)
2.0's wire format, which is a breaking change from the 1.x format the
preview-flag surface originally shipped against. Boutquin.Storage's own
migration guide has the full detail:
[`Boutquin.Storage` — Migration Guide 1.x → 2.0.0](https://github.com/boutquin/Boutquin.Storage/blob/main/docs/migration-1x-to-2x.md).

**Action required for any preview-flag adopter with a populated data
directory:** delete the stored time-series segments and re-ingest before
toggling `UseTimeSeriesStore` on against Boutquin.Storage 2.0. There is no
automatic 1.x → 2.0 data migration — the two wire formats are not
byte-compatible, and mixing them in one store directory produces a fail-fast
exception rather than silent corruption (see below). Because the time-series
tier is a rebuildable cache (every record is reconstructable from the source
adapter), delete-and-re-ingest is safe and is the only supported path.

**What happens if you don't delete first.** Boutquin.Storage 2.0's reader
tags every stored payload with a format version. The first read that reaches
a segment written by a pre-2.0 build raises
`Boutquin.Storage.Domain.Exceptions.UnsupportedFormatVersionException`. The
pipeline never swallows this exception — it re-throws the same exception type,
unwrapped, enriched with the affected record-type name and a reference back to
this section, so the caller sees the real failure at the `ExecuteAsync` call
site rather than a generic read error. If a store directory has a mix of 2.0
records and stray 1.x-written bytes (the "delete-and-re-ingest step was
skipped" case), every 2.0 record read *before* the 1.x bytes are reached
succeeds normally — only the read that reaches the incompatible bytes throws.

## Choosing the segment format

Standalone `TimeSeriesStoreBuilder` and `RowTimeSeriesStoreOptions` instances default their
segment root to `.marketdata-timeseries/{record-type}` beneath `AppContext.BaseDirectory`. This is
an absolute, application-local location and does not change with the process working directory.
`WithSegmentRoot(...)` and `RowStoreConfigurator.WithSegmentRoot(...)` override it. Kernel DI
composition continues to apply its configured root before a store is built.

Records implementing columnar rehydration use **columnar** segments by default
under the pipeline format setting
(`DataPipelineOptions.TimeSeriesSegmentFormat = TimeSeriesSegmentFormat.Columnar`).
`DividendPayment`, `FuturesSettlement`, and `OptionQuote` use row stores.
A per-record **row** opt-out is available through the store builder's
`UseRowFormat()` method for a record type whose access pattern benchmarks
better under the row layout.

Boutquin.Storage's own columnar guide covers the general row-vs-columnar
trade-off in depth:
[`Boutquin.Storage` — Columnar Time-Series Storage](https://github.com/boutquin/Boutquin.Storage/blob/main/docs/columnar-time-series.md).
Applied to MarketData's workload:

| Workload | Choose |
|---|---|
| Bulk-analytical reads projecting a few fields across many records — a backtest reading `(Date, AdjustedClose)` over years of `Bar` history | **Columnar** (default) |
| Long date ranges, few tickers, heavy `decimal`/`long` payloads — MarketData's typical read pattern | **Columnar** (default) |
| Cold-start deserialization time or working-set memory is the binding constraint | **Columnar** (default) |
| A record type whose payload shape is nullable-heavy and doesn't compress well under the columnar codecs (e.g. `OptionQuote`, `FuturesSettlement`, `DividendPayment` — see below) | **Row** |
| Per-key upserts on small windows, or a future record type that needs WiscKey key-value separation (see [WiscKey reconciliation](#wisckey-reconciliation)) | **Row** |

**Three shipped records are row-format by construction, not by opt-out.**
`OptionQuote`, `FuturesSettlement`, and `DividendPayment` carry nullable
`decimal` fields and wrapper types that the columnar source generator cannot
encode; they are `[StorageSerializable]`-only and register a row-format store
in the default kernel wiring rather than a columnar one. This is a
capability boundary, not a configuration choice a consumer can flip — the
Storage 2.0 columnar stack requires `IColumnarRehydratable<TRecord>`, which
only the `[ColumnarStorable]` source generator emits.

## Choosing the compaction strategy

The default compaction schedule is `TimeSeriesCompactionStrategy.SizeTiered` —
level-0 segments are grouped into size-tier buckets and the smallest bucket is
merged first. This matches MarketData's append-mostly workload: nightly and
intraday fetches append new dates to the tail of each dataset, which is
exactly the write pattern size-tiered compaction is designed for (Cassandra-style
bucketing).

| Strategy | When to choose it |
|---|---|
| `SizeTiered` (default) | The general MarketData case — append-mostly time series, nightly/intraday extend-by-one-day writes |
| `Leveled` | Read amplification is the primary concern and you can afford more write cost for better read locality (LevelDB-style level schedule) |
| `Full` | Small/test datasets only — merges every segment into one in a single pass; not recommended for production-sized datasets |

The compaction strategy is set per store through the DI builder's
`WithCompactionStrategy(TimeSeriesCompactionStrategy)` method.

## Quarantine and data-quality incidents

`DataPipelineOptions.QuarantineBehavior` (default `TreatAsGap`) is a
live policy applied by the DI-constructed columnar reader before records leave
the storage layer:

| Behavior | Effect on a quarantined row group |
|---|---|
| `TreatAsGap` (default) | The current request window is re-fetched from the adapter through the normal gap-fill path; the refetch is always intersected with the caller's range, the returned envelope is marked `Partial` with a quarantine issue, and a `DataQualityIncident` is emitted. A failed refetch remains eligible for recovery on the next call. |
| `SkipRowGroup` | Intact surrounding row groups are returned; unauthenticated rows are omitted and an incident is emitted. No adapter refetch is scheduled. |
| `FailQuery` | The CRC-mismatch exception surfaced by the Storage read path propagates to the `ExecuteAsync` caller. |

`DataPipelineOptions.DataQualityIncidentSink` accepts an optional
`IDataQualityIncidentSink` for routing `DataQualityIncident` records
(carrying the record type, row-group index, and the expected/actual CRC) to a
destination other than the default `ILogger` emission. A `DataQualityIncident`
is emit-and-forget: the pipeline hands one to the sink synchronously per
incident and retains no reference, so retention and archival are entirely the
sink implementation's responsibility. The reader suppresses duplicate reports
for the same observed CRC mismatch while still re-scheduling `TreatAsGap`
recovery until the damaged segment is retired or replaced.

## WiscKey reconciliation

WiscKey (key-value separation, DDIA 2e p. 130) is available in
Boutquin.Storage but **disabled by default** in every store the kernel
constructs (`DataPipelineOptions.TimeSeriesValueLog` defaults to `null`,
which disables value-log dispatch at the Storage engine boundary). Three reasons:

1. **MarketData's payloads are small.** A serialised `Bar` is roughly 60
   bytes; every in-scope record is flat. All of them sit far under any
   threshold at which key-value separation would help — WiscKey exists to
   eliminate the write-amplification cost of rewriting *large* values at every
   compaction level, and none of MarketData's values are large.
2. **The default columnar segment format rejects non-inline entries.** The
   columnar segment writer throws if it's asked to write a value-log pointer
   instead of an inline value. Leaving a finite value-log threshold in place
   on the default columnar wiring would mean any future oversized record
   silently crashes the flush path.
3. **A per-record opt-in exists for the rare exception.** A hypothetical
   future record type whose payload genuinely exceeds a few hundred bytes
   (for example, a large correlation-matrix snapshot) opts into WiscKey by
   opting out of columnar **and** setting a finite threshold in the same
   store-builder call — row format and value-log enablement must be set
   together. The builder rejects a half-configuration (value-log without row
   format) at construction time, so the columnar/WiscKey incompatibility
   cannot be triggered by accident.

## Advanced: adding a `[BitmapIndex]` column

Boutquin.Storage's columnar format supports a `[BitmapIndex]` attribute on a
low-cardinality column, emitting a footer-level bitmap index per distinct
value so a query can filter on that column without a full segment scan. None
of the record types shipped in this feature use it today — every current
column is either high-cardinality (dates, prices, decimals) or already
served well by the default per-dataset skip slots.

The attribute is available for a future record author who adds a
low-cardinality categorical column — for example a data-quality flag or a
venue code — where the per-distinct-value footer cost is worth paying for the
query-time filtering it buys. Annotate the column `[BitmapIndex]` on a
`[ColumnarStorable]` record; no other wiring is required, since the DI layer
already constructs stores generically from the record's own attributes.

## Qualified dependency candidate migration

The 2.0 RC uses the exact Storage candidate in `dependency-source.json`. Value-log
dispatch is disabled with `TimeSeriesValueLog = null`; row stores opt in using valid
`ValueLogOptions`. The old `int.MaxValue` threshold is no longer valid.

`TimeSeriesBloomFilter` now configures physical point-lookup filters in row and
columnar segments, including compaction output. Set `ExpectedItems` and
`FalsePositiveRate` to size new filters, or `Enabled = false` to omit them on new
writes. Existing segments reopen their persisted filters; range scans and range
tombstones remain independent of point membership.

For a custom implementation, call the typed builder's
`WithBloomFilter(factory, stateCodec)`. Each factory call must return a fresh
filter. The codec implements Storage's `IBloomFilterStateCodec<TimeSeriesKey<TDatasetKey>>`
with a stable ID/version and a fresh decoded filter on each call. The codec must
validate untrusted state and support concurrent calls. Storage owns and
disposes filter instances; the caller owns the codec. Keep the same codec available
when reopening, including when new filter writes are disabled.
`WithBloomFilterStateCodec(codec)` supports decoder-only reopening while new
writes use the built-in filter or no filter. Missing or mismatched codecs fail
opening without quarantining valid data. The factory-only overload still supports
`BuildBloomFilter()`; building a persistent store requires its codec.

Builders capture Bloom configuration when the stack opens, so later builder edits
cannot change an engine's compaction behavior. Numeric options also flow through
row-only registrations and `RowStoreConfigurator.WithBloomFilter(options)`.

Snapshot and raw-document stores retain read compatibility with earlier literal
filenames as well as the new Storage encoding. New writes take precedence without
rewriting old payloads. Legacy path components must match exactly; descendant
symbolic links are rejected. Application storage directories must remain under
the application's control. Invalid raw-document UTF-16 input and malformed stored
UTF-8 raise encoding exceptions instead of silently replacing evidence text.
