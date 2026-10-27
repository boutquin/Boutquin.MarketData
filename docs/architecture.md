# Boutquin.MarketData — Architecture

## Purpose

`Boutquin.MarketData` is the shared data ingestion kernel for three consumer libraries:

- **[Boutquin.Trading](https://github.com/boutquin/Boutquin.Trading)** — backtesting, portfolio construction, factor research
- **[Boutquin.Curves](https://github.com/boutquin/Boutquin.Curves)** — yield curves, fixings, conventions, bootstrap calibration
- **[Boutquin.OptionPricing](https://github.com/boutquin/Boutquin.OptionPricing)** — option models consuming curves and spot prices

The kernel prevents data-fetching duplication across consumers. FX rates, price bars, economic series, overnight fixings, and yield curve quotes are universal — every quantitative system needs them. Without a shared kernel, each consumer reimplements transport, caching, parsing, and provenance tracking independently.

---

## Three-Layer Architecture

```
Layer 1: Shared Ingestion Kernel (Boutquin.MarketData.*)
  Canonical records, typed requests, transport, caching, storage,
  normalization, provenance, fallback orchestration
  Concrete adapters live in the separate Boutquin.MarketData.Adapter repository

Layer 2: Domain Recipes/Builders (in each consumer repo)
  Boutquin.Trading.Recipes   — BacktestDatasetBuilder, BacktestDatasetSpec
  Boutquin.Curves.Recipes    — CurveBuilder, CurveRecipe, CurveSnapshot, CurveCalibrationInput

Layer 3: Application Code (in each consumer repo)
  Backtests, curve builds, pricing runs — depend on recipes, not adapters
```

### What lives where

| Concern | Location | Rationale |
|---------|----------|-----------|
| Canonical record types (Bar, FxRate, ScalarObservation, ...) | Kernel Abstractions | Universal — same shape regardless of consumer |
| Typed request records (PriceHistoryRequest, FxHistoryRequest, ...) | Kernel Abstractions | Request semantics are data-type-specific, not domain-specific |
| Concrete source adapters (Tiingo, Frankfurter, FRED, NY Fed, ...) | Boutquin.MarketData.Adapter repository | Emit kernel-level records; multiple consumers need the same sources |
| Transport, caching, storage, fallback | Kernel infrastructure packages | Generic plumbing shared by all adapters |
| Recipes/builders (`BacktestDatasetBuilder`, `CurveBuilder`) | Consumer domain repos | Compose kernel adapters into domain-specific bundles |
| Domain-specific adapters (CanDeal OTC quotes, legacy LIBOR) | Consumer domain repos | Only one domain needs them |

### The DRY test

When OptionPricing needs FX rates and discount curves, it should:

```csharp
services.AddMarketDataKernel();
services.AddMarketDataFrankfurter();          // FX rates — shared adapter
services.AddMarketDataNewYorkFed();           // SOFR fixings — shared adapter
services.AddMarketDataUsTreasury();           // Par yields — shared adapter
// Done — same adapters, same cache, same provenance as Trading and Curves
```

Not: copy-paste Frankfurter parsing from Trading, or take a dependency on Trading just for FX.

---

## Package Layering

```
Boutquin.MarketData.Abstractions              (core contracts + canonical records; shared Boutquin.Domain types)
  ^
Transport / Cache / Storage / Catalog         (infrastructure, depend on Abstractions and their external infrastructure libraries)
  ^
Adapter.Tiingo / .Frankfurter / .Fred         (concrete adapters for Trading-origin sources)
Adapter.BankOfCanada / .NewYorkFed            (concrete adapters for Curves-origin sources)
Adapter.UsTreasury / .BankOfEngland / .Ecb    (overnight fixings + yield curves)
Adapter.FamaFrench / .Cme / .TwelveData      (factor returns, futures, alternative equities)
  ^
Normalization / Orchestration                 (fallback, coverage evaluation)
  ^
DependencyInjection / Testing                 (composition root + test helpers)
```

Each adapter package depends only on `Abstractions` + `Transport` + `Storage`. No adapter references another adapter. Domain repos reference only the adapter packages they need.

---

## Canonical Record Types

Seven record families in `Abstractions/Records/`:

| Record | Fields | Used by |
|--------|--------|---------|
| `Bar` | Date, Open, High, Low, Close, AdjustedClose, Volume | Trading (backtests), OptionPricing (spot history) |
| `FxPair` | BaseCurrency, QuoteCurrency | All (currency pair identifier) |
| `FxRate` | Date, Rate (currency pair belongs to the dataset key) | Trading (multi-currency backtests), Curves (FX basis), OptionPricing (FX options) |
| `ScalarObservation` | Date, Value, Units | Trading (FRED economic series), Curves (overnight fixings), OptionPricing (risk-free rates) |
| `YieldCurveQuote` | Date, Tenor, Rate | Curves (curve construction), OptionPricing (discount curves) |
| `FuturesSettlement` | Date, ContractCode, ContractMonth, SettlePrice, ImpliedRate | Curves (SOFR futures strip) |
| `FactorObservation` | Date, FactorName, Value | Trading (Fama-French factor research) |

### Design decisions

- Records are **flat positional records** — no inheritance, no shared base fields. Provenance and coverage are carried by the `DataEnvelope<T>` wrapper, not embedded in each record.
- Records represent **normalized** values: rates in decimal form (0.045, not 4.5%), dates as `DateOnly`, volumes as `long`.
- Consumers project flat record lists into their own shapes (matrices, panels, curve nodes) — adapters never emit domain-specific bundles.

---

## Typed Request Records

Seven request types in `Abstractions/Requests/`, each implementing `IDataRequest`:

| Request | Key Fields | DatasetKey |
|---------|-----------|-----------|
| `PriceHistoryRequest` | Symbols, Range, Frequency | Sorted symbols joined with `+` |
| `FxHistoryRequest` | Pairs, Range, Frequency | Sorted pair strings joined with `+` |
| `EconomicSeriesRequest` | SeriesId, Range, Frequency | SeriesId |
| `OvernightFixingRequest` | BenchmarkId, Range, Frequency | BenchmarkId |
| `YieldCurveQuoteRequest` | CurveId, AsOfDate | CurveId |
| `FuturesSettlementRequest` | ProductCode, Range, Frequency | ProductCode |
| `FactorSeriesRequest` | DatasetName, Range, Frequency | DatasetName |

The `DatasetKey` is a deterministic string used for cache keys and logging. Request types are narrow and typed — no "fetch anything" interface.

---

## Adapter Inventory

### Implemented adapters

| Package | Adapter | Source API | Record Type | DI Extension |
|---------|---------|-----------|-------------|-------------|
| Adapter.Tiingo | `TiingoPriceBarAdapter` | Tiingo REST | `Bar` | `AddMarketDataTiingo(opts)` |
| Adapter.Frankfurter | `FrankfurterFxAdapter` | Frankfurter (ECB) | `FxRate` | `AddMarketDataFrankfurter()` |
| Adapter.Fred | `FredEconomicSeriesAdapter` | FRED REST | `ScalarObservation` | `AddMarketDataFred(opts)` |
| Adapter.BankOfCanada | `BankOfCanadaYieldCurveAdapter` | BoC Valet JSON | `YieldCurveQuote` | `AddMarketDataBankOfCanada()` |
| Adapter.BankOfCanada | `BankOfCanadaCorraAdapter` | BoC Valet JSON | `ScalarObservation` | (same DI call) |
| Adapter.NewYorkFed | `NewYorkFedSofrAdapter` | NY Fed REST | `ScalarObservation` | `AddMarketDataNewYorkFed()` |
| Adapter.UsTreasury | `UsTreasuryYieldAdapter` | Treasury XML | `YieldCurveQuote` | `AddMarketDataUsTreasury()` |
| Adapter.BankOfEngland | `BankOfEnglandSoniaAdapter` | BoE CSV | `ScalarObservation` | `AddMarketDataBankOfEngland()` |
| Adapter.Ecb | `EcbEstrAdapter` | ECB SDMX CSV | `ScalarObservation` | `AddMarketDataEcb()` |
| Adapter.FamaFrench | `FamaFrenchFactorAdapter` | FF ZIP/CSV | `FactorObservation` | `AddMarketDataFamaFrench()` |
| Adapter.Cme | `CmeSettlementAdapter` | Local CSV | `FuturesSettlement` | `AddMarketDataCme(opts)` |
| Adapter.TwelveData | `TwelveDataPriceBarAdapter` | Twelve Data REST | `Bar` | `AddMarketDataTwelveData(opts)` |

### Adapter conventions

Every adapter:
1. Implements `IDataSourceAdapter<TRequest, TRecord>` + `IPrioritizedAdapter`
2. Accepts `IHttpDataTransport`, `IRawDocumentStore`, `IClock`, `IOptions<TOptions>`, `ILogger<T>` via constructor
3. Saves raw API response via `IRawDocumentStore` before parsing (audit trail)
4. Returns `DataEnvelope<IReadOnlyList<TRecord>>` with coverage, provenance, and issues
5. Handles per-item errors gracefully (logs warning, adds `DataIssue`, continues)

### Fallback orchestration

The `FallbackOrchestrator<TRequest, TRecord>` tries adapters in `IPrioritizedAdapter.Priority` order (descending). The `IFallbackResponseEvaluator<T>` decides if a response is usable. The default `CoverageResponseEvaluator` accepts Complete and Partial, rejects Missing.

Multiple adapters for the same `<TRequest, TRecord>` pair coexist via `TryAddEnumerable` in DI — enabling automatic fallback (e.g., Tiingo primary → TwelveData fallback for price bars).

---

## Infrastructure

### Transport
`ResilientHttpDataTransport` wraps `HttpClient` with Polly v8:
- Exponential backoff retry with jitter (configurable attempts, base delay)
- Sliding-window rate limiting (configurable permits, window, segments)
- Structured logging of requests, retries, and completions

### Caching
- **L1** — `ExactOnceMemoryCache` uses in-process last-write-wins overwrites and per-entry TTL; `LazyAsyncCache` coalesces concurrent cold misses. Cache hits set `RetrievalMode="cache"` on provenance so callers can distinguish cached from fresh data.
- **L2** — `WriteThroughCache<TKey, TData>`: durable write-through to `ISnapshotStore`. L2 snapshots preserve `DataIssue` entries alongside `DataProvenance` so warnings survive cache round-trips.
- **Keys** — `CacheKeyBuilder`: deterministic, sorted, lexicographic

### Storage
- `FileSnapshotStore` — atomic temp+rename for canonical snapshots
- `FileRawDocumentStore` — atomic temp+rename for pre-normalization payloads
- `InMemorySnapshotStore` — for tests

### Provenance
Every `DataEnvelope<T>` includes:
- `DataProvenance` — provider, dataset, license flag, retrieval mode, freshness, timestamp, URL, and `DataDate` (the actual business date of data served — may differ from the requested date for EOD sources that publish with a lag)
- `DataCoverage` — requested/returned/missing points, coverage ratio, completeness status
- `DataIssue` — machine-readable code + severity + message

---

## Consumer Integration Pattern

### Trading

```csharp
services.AddMarketDataKernel();
services.AddMarketDataTiingo(opts => opts.ApiToken = "...");
services.AddMarketDataFrankfurter();
services.AddMarketDataFred(opts => opts.ApiKey = "...");
services.AddMarketDataFamaFrench();

// Trading's own recipe layer composes these into backtest datasets
services.AddSingleton<BacktestDatasetBuilder>();
```

### Curves

```csharp
services.AddMarketDataKernel();
services.AddMarketDataNewYorkFed();
services.AddMarketDataUsTreasury();
services.AddMarketDataBankOfCanada();
services.AddMarketDataBankOfEngland();
services.AddMarketDataEcb();
services.AddMarketDataCme(opts => opts.SettlementDirectory = "...");

// Boutquin.Curves' own recipe layer composes these into calibrated curve snapshots.
// See https://github.com/boutquin/Boutquin.Curves — Boutquin.Curves.Recipes.CurveBuilder.
```

### OptionPricing

```csharp
services.AddMarketDataKernel();
services.AddMarketDataFrankfurter();          // FX for FX options
services.AddMarketDataNewYorkFed();           // SOFR for discount curves
services.AddMarketDataUsTreasury();           // Par yields for curve context

// OptionPricing consumes Boutquin.Curves' calibrated recipes, not raw adapters.
// Discount curves come via Boutquin.Curves.Recipes.CurveBuilder
// (see https://github.com/boutquin/Boutquin.Curves).
```

---

## Time-Series Store Cache Tier

`DataPipeline` can route cache reads and gap-fill writes through a columnar LSM
time-series store (`Boutquin.Storage`'s `ITimeSeriesStore<TDatasetKey, TRecord>`)
in place of the legacy L2 snapshot store, behind the preview flag
`DataPipelineOptions.UseTimeSeriesStore` (default `false`). With the flag off,
the pipeline retains its existing L2 exact-key behaviour unchanged; with the
flag on, cache reads become coverage-aware — a request is served from the
store when its date range is already covered, and only the genuinely missing
dates trigger an adapter dispatch. See
[Time-Series Store Cache Tier](time-series-cache.md) for the full gap-fill
flow, snapshot-isolated reads for backtests and calibration, the segment
format and compaction choices, and the Boutquin.Storage 2.0 wire-format
migration note.

---

## What stays in domain repos

| Domain | What stays | Why |
|--------|-----------|-----|
| [Trading](https://github.com/boutquin/Boutquin.Trading) | `BacktestDatasetBuilder`, `BacktestDatasetSpec`, panel alignment, portfolio construction | Domain-specific bundle composition |
| [Curves](https://github.com/boutquin/Boutquin.Curves) | `CurveBuilder`, `CurveRecipe`, `CurveSnapshot`, `CurveCalibrationInput`, commercial adapter scaffolds (ICE, CanDeal, OTC), legacy adapters (LIBOR, CDOR), bootstrap calibration | Domain-specific governance, bootstrap, conventions |
| [OptionPricing](https://github.com/boutquin/Boutquin.OptionPricing) | Black-Scholes and beyond — closed-form pricing, binomial trees, implied volatility, calibrated smile surfaces | Domain-specific pricing model inputs |
