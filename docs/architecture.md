# Boutquin.MarketData — Architecture

## Purpose

`Boutquin.MarketData` is the shared data ingestion kernel for three consumer libraries:

- **Boutquin.Trading** — backtesting, portfolio construction, factor research
- **Boutquin.Analytics** — yield curves, fixings, conventions, valuation
- **Boutquin.OptionPricing** — option models consuming curves and spot prices

The kernel prevents data-fetching duplication across consumers. FX rates, price bars, economic series, overnight fixings, and yield curve quotes are universal — every quantitative system needs them. Without a shared kernel, each consumer reimplements transport, caching, parsing, and provenance tracking independently.

---

## Three-Layer Architecture

```
Layer 1: Shared Ingestion Kernel (Boutquin.MarketData.*)
  Canonical records, typed requests, transport, caching, storage,
  normalization, provenance, fallback orchestration, concrete adapters

Layer 2: Domain Recipes/Builders (in each consumer repo)
  Boutquin.Trading.Recipes   — BacktestDatasetBuilder, FactorResearchBuilder
  Boutquin.Analytics.Recipes    — EodCurveSnapshotBuilder, FixingsSnapshotBuilder

Layer 3: Application Code (in each consumer repo)
  Backtests, curve builds, pricing runs — depend on recipes, not adapters
```

### What lives where

| Concern | Location | Rationale |
|---------|----------|-----------|
| Canonical record types (Bar, FxRate, ScalarObservation, ...) | Kernel Abstractions | Universal — same shape regardless of consumer |
| Typed request records (PriceHistoryRequest, FxHistoryRequest, ...) | Kernel Abstractions | Request semantics are data-type-specific, not domain-specific |
| Concrete source adapters (Tiingo, Frankfurter, FRED, NY Fed, ...) | Kernel Adapter.* packages | Emit kernel-level records; multiple consumers need the same sources |
| Transport, caching, storage, fallback | Kernel infrastructure packages | Generic plumbing shared by all adapters |
| Recipes/builders (BacktestDatasetBuilder, EodCurveSnapshotBuilder) | Consumer domain repos | Compose kernel adapters into domain-specific bundles |
| Domain-specific adapters (CanDeal OTC quotes, legacy LIBOR) | Consumer domain repos | Only one domain needs them |

### The DRY test

When OptionPricing needs FX rates and discount curves, it should:

```csharp
services.AddMarketDataKernel();
services.AddMarketDataFrankfurter();          // FX rates — shared adapter
services.AddMarketDataNewYorkFed();           // SOFR fixings — shared adapter
services.AddMarketDataUsTreasury();           // Par yields — shared adapter
// Done — same adapters, same cache, same provenance as Trading and Analytics
```

Not: copy-paste Frankfurter parsing from Trading, or take a dependency on Trading just for FX.

---

## Package Layering

```
Boutquin.MarketData.Abstractions              (core contracts + canonical records, zero dependencies)
  ^
Transport / Cache / Storage / Catalog         (infrastructure, depend on Abstractions only)
  ^
Adapter.Tiingo / .Frankfurter / .Fred         (concrete adapters for Trading-origin sources)
Adapter.BankOfCanada / .NewYorkFed            (concrete adapters for Analytics-origin sources)
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
| `FxRate` | Date, BaseCurrency, QuoteCurrency, Rate | Trading (multi-currency backtests), Analytics (FX basis), OptionPricing (FX options) |
| `ScalarObservation` | Date, Value, Units | Trading (FRED economic series), Analytics (overnight fixings), OptionPricing (risk-free rates) |
| `YieldCurveQuote` | Tenor, Rate | Analytics (curve construction), OptionPricing (discount curves) |
| `FuturesSettlement` | Date, ContractCode, ContractMonth, SettlePrice, ImpliedRate | Analytics (SOFR futures strip) |
| `FactorObservation` | Date, Factors (dictionary) | Trading (Fama-French factor research) |

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
- **L1** — `ExactOnceMemoryCache` / `LazyAsyncCache`: in-process, exact-once materialization. Cache hits set `RetrievalMode="cache"` on provenance so callers can distinguish cached from fresh data.
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

### Analytics

```csharp
services.AddMarketDataKernel();
services.AddMarketDataNewYorkFed();
services.AddMarketDataUsTreasury();
services.AddMarketDataBankOfCanada();
services.AddMarketDataBankOfEngland();
services.AddMarketDataEcb();
services.AddMarketDataCme(opts => opts.SettlementDirectory = "...");

// Analytics' own recipe layer composes these into curve snapshots
services.AddSingleton<EodCurveSnapshotBuilder>();
```

### OptionPricing

```csharp
services.AddMarketDataKernel();
services.AddMarketDataFrankfurter();          // FX for FX options
services.AddMarketDataNewYorkFed();           // SOFR for discount curves
services.AddMarketDataUsTreasury();           // Par yields for curve context

// OptionPricing consumes Analytics' curve recipes, not raw adapters
// Discount curves come via Boutquin.Analytics.Recipes.EodCurveSnapshotBuilder
```

---

## What stays in domain repos

| Domain | What stays | Why |
|--------|-----------|-----|
| Trading | `BacktestDatasetBuilder`, `PortfolioInputSnapshotBuilder`, panel alignment, CSV symbol reader | Domain-specific bundle composition |
| Analytics | `EodCurveSnapshotBuilder`, `MarketDataAggregationService`, commercial adapter scaffolds (ICE, CanDeal, OTC), legacy adapters (LIBOR, CDOR), curve construction | Domain-specific governance, bootstrap, conventions |
| OptionPricing | `OptionMarketData`, volatility surface calibration, dividend schedule construction | Domain-specific pricing model inputs |
