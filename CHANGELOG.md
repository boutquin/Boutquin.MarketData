# Changelog

All notable changes to `Boutquin.MarketData` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Versions are produced by [MinVer](https://github.com/adamralph/minver) from git tags on the public release repository.

## [Unreleased]

_No unreleased changes yet. See 1.0.0 below for the initial public release._

## [1.0.0] — 2026-04-17

First public release. Boutquin.MarketData ships a shared data ingestion kernel for the Boutquin financial-software ecosystem: typed transport, multi-level caching, durable storage, deterministic normalization, business-day calendars, day-count conventions, ISDA instrument conventions, and a full canonical reference-data taxonomy — all with zero dependency on Analytics, Trading, or OptionPricing. Concrete source adapters live in the companion `Boutquin.MarketData.Adapter` repository; the kernel ships contracts and infrastructure only.

### Highlights

- **Full eight-step pipeline with two-level caching.** `DataPipeline.FetchAsync` executes: L1 in-memory cache check → L2 snapshot check → adapter selection → fetch → normalize → L2 write → L1 promote → return. Issue entries (`DataIssue`) persist through L2 round-trips so warnings survive cache hits. L1 cache misses set `RetrievalMode.Cache` on provenance so callers can distinguish cached from fresh data.
- **Typed reference data across two tiers.** Tier 1 covers core market identifiers: `CurrencyCode` (ISO 4217, numeric values stable for persistence), `ExchangeCode` (ISO 10383 MIC), `AssetClassCode`, `SecuritySymbolStandard`, and `Symbol`. Tier 2 adds market-structure taxonomies: `CountryCode` (ISO 3166-1), `ContinentCode`, `TimeZoneCode`, and `DividendType` (eight issuer-declared categories, no sentinel). `ExchangeMetadata` and `CountryMetadata` provide exhaustive static lookup with a compile-error on synthetic casts.
- **Typed provenance and diagnostics.** `DataProvenance` carries `ProviderCode` (case-insensitive via lower-case normalisation), `LicenseType`, and `RetrievalMode`. `DataIssue` carries `IssueCode` (upper-case normalisation, eight well-known statics) and `IssueSeverity`. Mix-ups that previously required string comparison are now compile errors.
- **Typed external identifiers.** Five strongly-typed record structs — `YieldCurveId`, `EconomicSeriesId`, `FactorDatasetId`, `FuturesProductCode`, and `ContractMonth` — eliminate cross-request-type mix-ups at the call site. Assigning a `YieldCurveId` to a `FactorDatasetId` field is a `CS0029` diagnostic, not a silent runtime error.
- **Transport hardened against SSRF, response-size attacks, and credential leaks.** `ResilientHttpDataTransport` validates every URI before dispatch (scheme, loopback, RFC-1918, link-local, CGNAT, and hostname blocklist), enforces a configurable `MaxResponseBytes` cap (default 32 MiB) with streaming abort, and redacts configured query-parameter names in debug logs. Retry is narrowed to genuine transport failures; owned `HttpClient` uses `PooledConnectionLifetime = 2 minutes` for DNS-change resilience.
- **Zero domain dependencies, enforced.** Architecture tests assert that no assembly in the kernel references `Boutquin.Analytics`, `Boutquin.Trading`, or `Boutquin.OptionPricing`, and that concrete `System.IO` types (`File`, `Directory`, `FileStream`, …) are confined to the Storage package. The Coverlet instrumentation class is excluded from all architecture assertions per the documented coverlet/NetArchTest workaround.

### Added

#### Core Contracts (`Boutquin.MarketData.Abstractions`)

- **Canonical records** under `Records/`: `Bar` (OHLCAV price bar), `FxPair` / `FxRate` (currency pair and spot rate), `ScalarObservation` (single-value time series), `YieldCurveQuote` (tenor + rate), `FuturesSettlement` (settlement price with typed `FuturesProductCode` and `ContractMonth`), `InstrumentMetadata` (symbol, exchange, asset class, symbol standard), `DividendPayment` (ex-date, amount, currency, `DividendType`), `CorporateAction`, `OptionQuote`, `FactorObservation`.
- **Typed request models** under `Requests/`: `PriceHistoryRequest`, `FxHistoryRequest`, `EconomicSeriesRequest`, `YieldCurveQuoteRequest`, `InstrumentMetadataRequest`, `OvernightFixingRequest`, `FactorSeriesRequest`, `FuturesSettlementRequest`. All symbol and pair collections are defensively copied at construction so post-construction mutation cannot affect `DatasetKey` stability or record equality.  `DateRange` rejects inverted ranges (`To < From`) at construction.
- **Pipeline contracts**: `IDataRequest`, `IDataSourceAdapter<TRequest, TRecord>`, `IPrioritizedAdapter`, `IFallbackResponseEvaluator<TResponse>`, `IDataPipeline`, `ILocalCache`, `ISnapshotStore`, `INormalizer<TRaw, TCanonical>`, `IFixingsStore`.
- **Tier 1 reference-data enumerations**: `CurrencyCode` (ISO 4217 numeric values — USD=840, EUR=978, …), `ExchangeCode` (ISO 10383 MIC), `AssetClassCode`, `SecuritySymbolStandard`. `Symbol` readonly record struct: null/whitespace-rejecting, `IComparable<Symbol>`, ordinal ticker ordering.
- **Tier 2 market-structure taxonomies**: `CountryCode` (ISO 3166-1 alpha-2, 11 members), `ContinentCode` (UN geoscheme, 7 members), `TimeZoneCode` (ISO 8601, 9 standard-time designators), `DividendType` (8 issuer-declared categories — no `Unknown` sentinel, adapter classification is mandatory). `ExchangeMetadata.GetTimeZone(ExchangeCode)` and `.GetCountry(ExchangeCode)` — exhaustive, thread-safe, throw `ArgumentOutOfRangeException` on synthetic casts. `CountryMetadata.GetContinent(CountryCode)` — same contract. `IDividendClassifier` contract: never throws on well-formed inputs, never returns `default`, broad-fallback rules documented.
- **Typed provenance** under `Provenance/`: `ProviderCode` readonly record struct with lower-case normalisation and `System.Text.Json` round-trip support; `LicenseType` enum (`Free`, `DelayedFifteenMin`, `Commercial`, `ResearchOnly`, `Redistribution`); `RetrievalMode` enum (`Api`, `Cache`, `Snapshot`, `Derived`, `Fixture`).
- **Typed diagnostics** under `Diagnostics/`: `IssueSeverity` enum (`Warning`, `Error`); `IssueCode` readonly record struct with upper-case normalisation and eight well-known statics — `MissingField`, `StaleData`, `RateLimited`, `HolidayGap`, `UnexpectedGap`, `SchemaDrift`, `SnapshotStale`, `PartitionFailed`.
- **Typed external identifiers** under `ReferenceData/`: `YieldCurveId`, `EconomicSeriesId`, `FactorDatasetId`, `FuturesProductCode` (thin `Symbol`-style wrappers, null/whitespace-rejecting, case-preserving); `ContractMonth` (structured `(Year, Month)` with canonical `YYYY-MM` rendering, string constructor accepting both `YYYY-MM` and `YYYYMM`, implicit `DateOnly` conversion to first-of-month for sort ergonomics).
- **Rate benchmarks**: `RateBenchmark`, `BenchmarkKind`, `BenchmarkName`, `StandardBenchmarks` — six pre-built benchmark instances covering SOFR, SONIA, ESTR, CORRA, SARON (CHF), and TONA (JPY) with associated calendar codes and day-count conventions (Hull 11e Ch 4 p.99–100).
- **`ISecurityResolver`** — dual-listing resolver contract with identity-on-miss, reflexive, and commutative invariants for unknown symbols; `AllListings` returns `IReadOnlyCollection<Symbol>`.
- **Exception hierarchy** under `Exceptions/`: abstract `MarketDataException` base plus sealed `MarketDataNotFoundException`, `MarketDataRetrievalException`, `MarketDataProcessingException`, `MarketDataStorageException`, `SymbolNotFoundException`. Every type supports `(string)` and `(string, Exception)` constructors for inner-exception chaining.
- **`DataEnvelope<TRecord>`** wrapping records with `DataProvenance` and a `IReadOnlyList<DataIssue>` collection. `FallbackResult<TResponse>` and `FallbackAttempt` carry `ProviderCode` throughout the fallback chain.
- **`CoverageComputation`** — coverage aggregation using `long` accumulators with a single pass and a `checked` narrowing cast; enforces the canonical `MissingPoints = max(0, requested − returned)` identity.

#### Transport (`Boutquin.MarketData.Transport`)

- **`ResilientHttpDataTransport`** implementing `IHttpDataTransport` and `IAsyncDisposable`: Polly v8 resilience pipeline with retry and exponential backoff; URI validation blocking SSRF vectors (loopback, link-local, RFC-1918, CGNAT, ULA, and hostname blocklist) with opt-in `TransportOptions.AllowPrivateNetworks`; `MaxResponseBytes` cap (default 32 MiB) enforced in both the pre-flight `Content-Length` check and the streaming-copy loop, aborting and disposing the response on cap breach; debug-log redaction of query parameters listed in `TransportOptions.SensitiveQueryParameters` (defaults cover `token`, `apikey`, `api_key`, `apitoken`, `api_token`, `secret`, `access_token`, `auth`); retry predicate narrowed to transport-level failures only; owned `HttpClient` uses `SocketsHttpHandler.PooledConnectionLifetime = 2 minutes`; does not mutate a caller-owned `HttpClient.Timeout`; `IAsyncDisposable` implementation with `Interlocked.Exchange` idempotency guard disposing the owned `SlidingWindowRateLimiter` without blocking on in-flight permits.

#### Cache (`Boutquin.MarketData.Cache`)

- **`ExactOnceMemoryCache`** implementing `ILocalCache`: TTL-based in-memory cache with atomic TOCTOU-narrowed expiry reclaim and type-safe retrieval (returns `default` on type mismatch, never throws `InvalidCastException`).
- **`WriteThroughCache`** — write-through wrapper over `ISnapshotStore` using `await using` on every `ISnapshotStore.ReadAsync` result to prevent blocking disposal on file-backed streams.

#### Storage (`Boutquin.MarketData.Storage`)

- **`FileSnapshotStore`** and **`InMemorySnapshotStore`** implementing `ISnapshotStore` with atomic writes.
- **`SnapshotEnvelopeSerializer`** — 1 MiB upper bound on the metadata JSON length prefix; rejects out-of-range values with `InvalidDataException` before any allocation, preventing negative-length or gigabyte-scale allocation from a corrupted or hostile payload.
- **`IngestionCheckpointStore`** — deserialised checkpoints are rejected when `SymbolIndex < 0`, `Provider` is empty, or `Dataset` is empty; `JsonException` during deserialisation is caught and re-raised with pipeline context.
- **`InMemoryFixingsStore`** implementing `IFixingsStore`: last-in-wins seeding, keyed by `BenchmarkName` + `DateOnly`, returning `decimal?` or `TimeSeriesPoint<decimal>`.
- **`FileRawDocumentStore`** implementing `IRawDocumentStore`: explicit path-traversal defense rejecting `..`/`.` segments, empty segments, absolute-rooted keys, and drive-letter prefixes before delegating to the underlying store; defense documented as an interface-level contract on `IRawDocumentStore.SaveAsync`.

#### Catalog (`Boutquin.MarketData.Catalog`)

- **`SourceRegistry<TRequest, TRecord>`** — provider registration with priority ordering and `ProviderCode`-keyed lookup (case-insensitive); constructor null-checks the adapter collection and each element, re-raising duplicate-key exceptions with the full key list.
- **`DatasetKey`** record-struct rejects null/whitespace values via `ArgumentException.ThrowIfNullOrWhiteSpace`.

#### Calendars (`Boutquin.MarketData.Calendars`)

- **`IBusinessCalendar`** contract with `IsBusinessDay(DateOnly)`, `Advance(DateOnly, int)`, and `CountBusinessDays(DateOnly, DateOnly)`. `BusinessCalendarBase` rejects `int.MinValue` steps with `ArgumentOutOfRangeException` (avoiding `Math.Abs(int.MinValue)` overflow).
- **`HolidayCalendar`** — full holiday-rule calendar with null-checked constructor and immutable holiday set via collection expression.
- **`HolidayCalendarFactory`** backed by an `ImmutableArray<string>` code list: `USNY`, `GBLO`, `TARGET`, `CATO`, `XCME`, `USSF`, `CAST` delivered as full holiday-rule calendars; `ZURH` and `TKYO` served by `WeekendOnlyCalendar` fallback so `StandardBenchmarks.ChfSaron` and `.JpyTona` resolve without an ad-hoc dataset.
- **`CalendarHelpers.ComputeEaster`** — rejects years outside `[1583, 9999]` (Gregorian adoption floor / `DateOnly` upper bound). **`NthDayOfWeek`** rejects `n` outside `[1, 5]`.

#### Day-Count Conventions (`Boutquin.MarketData.DayCount`)

- Five conventions implementing `IDayCountConvention`: `Actual360` (ACT/360), `Actual365Fixed` (ACT/365F), `ActualActualInPeriod` (ICMA Rule 251 — US Treasury / euro government bonds; ICMA ACT/365.25 fallback when no coupon context provided), `Thirty360` (ISDA 2006 §4.16(f) Bond Basis — D2 is capped to 30 only when capped D1 is already 30 and D2 is 31, distinguishing Bond Basis from 30E/360 on spans where D2 = 31 and D1 < 30), `ThirtyE360` (ISDA 2006 §4.16(g) Eurobond — unconditional D1/D2 cap to 30).
- All conventions reject `endDate < startDate` with `ArgumentOutOfRangeException`; negative year fractions almost always indicate a schedule-generation bug.
- **`DayCountResolver`** — trims and case-folds input; resolves aliases `ACT/ACT`, `ACTUAL/ACTUAL`, `ACT/ACT ICMA`, `ACT/ACT BOND`, `30E/360`, `EUROBOND`, `EUROBOND BASIS`.

#### Instrument Conventions (`Boutquin.MarketData.Conventions`)

- **`InstrumentConventionRegistry`** with pre-built `CreateDefault()` producing four-currency OIS, FRA, futures, and swap-leg catalogues.
- Records: `OisConvention`, `FraConvention`, `SwapLegConvention`, `FutureContractConvention`, `SettlementConvention`. Supporting enums: `PaymentFrequency`, `RollConvention`, `StubConvention`, `ObservationShiftConvention`. Interface `IInstrumentConvention`. No dependency on `Boutquin.Curves` — architecture tests enforce isolation.

#### Normalization (`Boutquin.MarketData.Normalization`)

- Ten normalizers implementing `INormalizer<TRaw, TCanonical>`: `BarNormalizer`, `FxRateNormalizer`, `ScalarObservationNormalizer` (with public `const CanonicalDecimalUnits` for caller reference), `YieldCurveQuoteNormalizer`, `FuturesSettlementNormalizer`, `InstrumentMetadataNormalizer`, `DividendPaymentNormalizer`, `CorporateActionNormalizer`, `OptionQuoteNormalizer`, `FactorObservationNormalizer`. All guard their `raw` input with `ArgumentNullException.ThrowIfNull`.

#### Orchestration (`Boutquin.MarketData.Orchestration`)

- **`DataPipeline`** implementing `IDataPipeline`: eight-step orchestration (validate → L1 check → L2 check → adapter select → fetch with fallback → normalize → L2 write → L1 promote). When `ContinueOnPartitionFailure = false` and a partition fails, the L2 write and L1 promotion are skipped — a partial batch is not cached as authoritative. **`CoverageResponseEvaluator`** null-guards the response before dereferencing `Payload` and accepts an optional `MinimumCoverageRatio` constructor parameter for strict-mode pipelines.

#### Dependency Injection (`Boutquin.MarketData.DependencyInjection`)

- **`AddMarketDataKernel`** — idempotent `IServiceCollection` extension registering transport, L1 cache, L2 write-through cache, and pipeline for all ten canonical record types (`Bar`, `FxRate`, `ScalarObservation`, `YieldCurveQuote`, `FuturesSettlement`, `InstrumentMetadata`, `DividendPayment`, `CorporateAction`, `FactorObservation`, `OptionQuote`). **`AddMarketDataWriteThroughFor<TRecord>()`** extension for downstream repos registering custom record types.

#### Testing (`Boutquin.MarketData.Testing`)

- **`EnvelopeBuilder`** — fluent builder for `DataEnvelope<TRecord>` test fixtures accepting typed `ProviderCode`, `LicenseType`, `RetrievalMode`, `IssueCode`, and `IssueSeverity`. Default envelope carries `LicenseType.Free` + `RetrievalMode.Fixture`.
- **`FakeDataSourceAdapter`** — produces a fresh `HttpResponseMessage` per `SendAsync` via a factory closure, eliminating the test-flake pattern caused by a shared-instance content stream being exhausted on second call.
- **`FakeHttpMessageHandler`** — deterministic HTTP message handler for adapter unit tests.

#### Validation

- Architecture tests enforce: zero references to `Boutquin.Analytics`, `Boutquin.Trading`, or `Boutquin.OptionPricing` from any kernel assembly; concrete `System.IO` types confined to the Storage package; `Coverlet.Core.Instrumentation` excluded from all assertions per the repo-wide coverlet/NetArchTest workaround (see `CONTRIBUTING.md`).
- Documentation-quality CI gates in `pr-verify.yml` reject prohibited-phrase, low-signal-phrase, and accessor-verb patterns across `src/**/*.cs` before merge.

### Documentation

- [`docs/architecture.md`](docs/architecture.md) — layered package design, `DataPipeline` internals, three-layer data architecture constraint, and design decisions.
- [`docs/reference-data.md`](docs/reference-data.md) — Tier 1 and Tier 2 enumerations, `Symbol`, `ExchangeMetadata` / `CountryMetadata` lookup contracts, rate benchmarks, instrument conventions, fixings, exception hierarchy, `Provenance` / `Diagnostics` typed wrappers, external identifiers, and the migration table from the pre-typed string fields.
- [`docs/day-counts.md`](docs/day-counts.md) — shipped conventions, alias table, `ActualActualInPeriod` ICMA guidance, and the `Thirty360` Bond Basis vs. `ThirtyE360` Eurobond distinction with a worked discriminating example.
- `README.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md` — repository scaffolding.
