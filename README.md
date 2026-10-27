# Boutquin.MarketData

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE.txt)
[![Build](https://github.com/boutquin/Boutquin.MarketData/actions/workflows/pr-verify.yml/badge.svg)](https://github.com/boutquin/Boutquin.MarketData/actions/workflows/pr-verify.yml)

Shared data ingestion kernel for [Boutquin.Curves](https://github.com/boutquin/Boutquin.Curves), [Boutquin.Trading](https://github.com/boutquin/Boutquin.Trading), and [Boutquin.OptionPricing](https://github.com/boutquin/Boutquin.OptionPricing). Owns transport, caching, storage, normalization, pipeline orchestration, calendars, day-count conventions, and all canonical record and reference-data types — domain repositories own their builders and recipes that compose kernel adapters into domain-specific bundles.

Concrete source adapters (Tiingo, Frankfurter, FRED, Bank of Canada, etc.) live in [Boutquin.MarketData.Adapter](https://github.com/boutquin/Boutquin.MarketData.Adapter). The kernel ships contracts and infrastructure only — adapters are plug-ins, not dependencies.

**2.0 RC:** targets .NET 10 and published Storage 2.0.0. Install the explicit prerelease version below; unversioned installs select the older stable release. See [migration](docs/migration-2.0.md) and [building and release verification](docs/runner-dependencies.md).

## Solution Structure

| Project | Description |
|---------|-------------|
| **Boutquin.MarketData.Abstractions** | Core contracts, canonical records, typed request/response models, reference-data taxonomies, `Provenance`, `Diagnostics`, external identifiers, exception hierarchy |
| **Boutquin.MarketData.Transport** | HTTP client abstraction with Polly v8 resilience, SSRF defense, response-size caps, and secret redaction in debug logs |
| **Boutquin.MarketData.Cache** | L1 in-memory cache with TTL, last-write-wins overwrites, and `RetrievalMode.Cache` provenance tagging |
| **Boutquin.MarketData.Storage** | L2 durable snapshot store with atomic writes, `InMemoryFixingsStore`, and streaming-cap DoS defense |
| **Boutquin.MarketData.Catalog** | Provider and dataset registry — adapter registration, priority ordering, and key lookup |
| **Boutquin.MarketData.Calendars** | Business-day calendars: USNY, GBLO, TARGET, CATO, XCME, USSF, USSOFR, CAST; ZURH and TKYO via weekend-only fallback |
| **Boutquin.MarketData.DayCount** | Day-count conventions: ACT/360, ACT/365F, ACT/ACT (ICMA in-period), 30/360 Bond Basis (ISDA 2006 §4.16(f)), 30E/360 Eurobond (ISDA 2006 §4.16(g)) |
| **Boutquin.MarketData.Conventions** | ISDA-style instrument conventions: `OisConvention`, `FraConvention`, `SwapLegConvention`, `FutureContractConvention`, `SettlementConvention`, `InstrumentConventionRegistry` |
| **Boutquin.MarketData.Normalization** | Per-record normalizers: percent-to-decimal conversion, rounding, and canonical field cleanup |
| **Boutquin.MarketData.Orchestration** | `DataPipeline` — L1 cache → source selection → fetch → normalize → L2 snapshot → L1 promote |
| **Boutquin.MarketData.DependencyInjection** | `IServiceCollection` composition helpers; `AddMarketDataKernel` wires all ten record-type caches, the pipeline, and the transport layer |
| **Boutquin.MarketData.Testing** | `EnvelopeBuilder`, `FakeDataSourceAdapter`, `FakeHttpMessageHandler`, and fixture helpers for test isolation |

**Test projects** — 14 total: one unit-test project per source package, `Boutquin.MarketData.Tests.Integration` (full pipeline round-trips), and `Boutquin.MarketData.ArchitectureTests` (zero-domain-dependency and file-system-access enforcement).

## Architecture

```
Boutquin.MarketData.Abstractions      Core contracts + canonical records (Boutquin.Domain value objects)
  ↑
Transport / Cache / Storage           Infrastructure — HTTP, L1 cache, L2 snapshots
Catalog                               Provider registry
DayCount / Calendars / Conventions    Shared math and scheduling primitives
  ↑
Normalization / Orchestration         Pipeline — unit conversion, source selection, caching
  ↑
DependencyInjection / Testing         Composition helpers and test fixtures

Boutquin.MarketData.Adapter (separate repo)
  └── Tiingo / Frankfurter / FRED / BankOfCanada / … (11 adapters)
```

**Key constraint:** MarketData must not reference [Curves](https://github.com/boutquin/Boutquin.Curves), [Trading](https://github.com/boutquin/Boutquin.Trading), or [OptionPricing](https://github.com/boutquin/Boutquin.OptionPricing). It is the shared kernel that all three consume.

## Reference Data

### Tier 1 — Core Enumerations (`Abstractions/ReferenceData/`)

Canonical market identifiers shared across the entire Boutquin ecosystem. Numeric values are stable — persisted records and network formats depend on them.

| Type | Standard | Members |
|------|----------|---------|
| `Boutquin.Domain.Currency` | ISO 4217 (numeric values) | 177 currencies plus `Unspecified` (USD=840, EUR=978, …) |
| `ExchangeCode` | ISO 10383 MIC | XNYS, XNAS, XLON, XPAR, XTSE, XCME, … |
| `AssetClassCode` | — | CashAndCashEquivalents, FixedIncome, Equities, RealEstate, Commodities, Alternatives, CryptoCurrencies, Other |
| `SecuritySymbolStandard` | — | Cusip, Isin, Sedol, Ric, BloombergTicker |
| `Symbol` | — | Readonly record struct; null/whitespace-rejecting; ordinal ticker ordering |

### Tier 2 — Market Structure Taxonomies

| Type | Standard | Members |
|------|----------|---------|
| `CountryCode` | ISO 3166-1 alpha-2 | 11 members |
| `ContinentCode` | UN geoscheme | 7 members |
| `TimeZoneCode` | ISO 8601 | 9 standard-time designators |
| `DividendType` | — | 7 issuer-declared categories plus an `Unclassified` fallback for provider-aggregated, multi-category amounts |

`ExchangeMetadata.GetTimeZone(ExchangeCode)` and `CountryMetadata.GetContinent(CountryCode)` provide exhaustive static lookup with `ArgumentOutOfRangeException` on synthetic casts.

### Rate Benchmarks

`StandardBenchmarks` ships six pre-built `RateBenchmark` instances covering the major overnight benchmarks — SOFR, SONIA, ESTR, CORRA, SARON, TONA — with the associated `BenchmarkKind`, calendar codes, and day-count conventions. `IFixingsStore` / `InMemoryFixingsStore` support keyed fixings retrieval by `BenchmarkName` + `DateOnly`.

### External Identifiers

Five strongly-typed wrappers prevent mix-ups between request types:

| Wrapper | Used in |
|---------|---------|
| `YieldCurveId` | `YieldCurveQuoteRequest.CurveId` |
| `EconomicSeriesId` | `EconomicSeriesRequest.SeriesId` |
| `FactorDatasetId` | `FactorSeriesRequest.DatasetName` |
| `FuturesProductCode` | `FuturesSettlementRequest.ProductCode`, `FuturesSettlement.ContractCode` |
| `ContractMonth` | `FuturesSettlement.ContractMonth` — structured `(Year, Month)` with canonical `YYYY-MM` rendering |

Assigning a `YieldCurveId` to a `FactorDatasetId` field is now a compile error, not a silent runtime mix-up.

## Pipeline

`DataPipeline.ExecuteAsync<TRequest, TRecord>` executes the canonical kernel path:

1. Validate request
2. Check L1 in-memory cache → if hit, tag `RetrievalMode.Cache` and return
3. Check L2 snapshot store → if hit and not stale, return with issue preservation
4. Select adapters by priority order from `SourceRegistry`
5. Fetch from adapters with configurable fallback and `IFallbackResponseEvaluator`
6. Normalize with the registered `INormalizer<TRaw, TRecord>`
7. Write to L2 snapshot (skipped on partial-batch failure when `ContinueOnPartitionFailure=false`)
8. Promote to L1 cache
9. Return `DataEnvelope<TRecord>` with `DataProvenance` and any `DataIssue` entries

**Issue preservation** — L2 snapshots persist `DataIssue` entries alongside `DataProvenance` so warnings (stale data, missing fields, rate limits) survive cache round-trips.

**Provenance typing** — `DataProvenance` carries `ProviderCode` (lower-case normalisation; `new ProviderCode("Tiingo")` and `new ProviderCode("TIINGO")` compare equal), `LicenseType`, and `RetrievalMode`. `DataIssue` carries `IssueCode` (upper-case normalisation, with well-known statics: `IssueCode.StaleData`, `IssueCode.MissingField`, `IssueCode.RateLimited`, etc.) and `IssueSeverity`.

### Time-series store cache tier (preview)

Behind `DataPipelineOptions.UseTimeSeriesStore` (default `false`), the pipeline can route cache reads and gap-fill writes through a columnar LSM time-series store instead of the L2 exact-key snapshot store — a request whose date range is already covered returns with zero adapter calls, and only genuinely missing dates trigger a fetch. The tier also adds snapshot-isolated reads (`IDataPipeline.AcquireReadSnapshotAsync`) for backtests and calibration runs that need a coherent point-in-time view. See [`docs/time-series-cache.md`](docs/time-series-cache.md) for the full flow, the segment-format and compaction choices, and the Boutquin.Storage 2.0 migration note.

### Default normalizer registration (opt-in)

Behind `DataPipelineOptions.RegisterDefaultNormalizers` (default `false`, alongside `UseTimeSeriesStore` above), `AddMarketDataKernel` registers all nine shipped normalizers — one per canonical record type — so step 6 of the pipeline above actually runs instead of silently passing records through un-normalized. A normalizer a host registers itself always wins over the kernel default, in either registration order. See [`docs/normalization.md`](docs/normalization.md) for what each normalizer does, override/opt-out recipes, and a cache-coherence note for hosts with an already-populated cache directory.

## Typed Requests

Each typed request drives `IDataPipeline.ExecuteAsync<TRequest, TRecord>` and returns exactly one canonical record type. Sources ship in the companion [Boutquin.MarketData.Adapter](https://github.com/boutquin/Boutquin.MarketData.Adapter) package family.

| Request | Record | Shipped sources |
|---------|--------|-----------------|
| `PriceHistoryRequest` | `Bar` | Tiingo, TwelveData |
| `DividendHistoryRequest` | `DividendPayment` | Tiingo |
| `FxHistoryRequest` | `FxRate` | Frankfurter, Bank of Canada |
| `EconomicSeriesRequest` | `ScalarObservation` | FRED |
| `OvernightFixingRequest` | `ScalarObservation` | New York Fed (SOFR), Bank of England (SONIA), ECB (€STR), Bank of Canada (CORRA) |
| `YieldCurveQuoteRequest` | `YieldCurveQuote` | US Treasury, Bank of Canada |
| `FuturesSettlementRequest` | `FuturesSettlement` | CME |
| `FactorSeriesRequest` | `FactorObservation` | Fama-French |
| `InstrumentMetadataRequest` | `InstrumentMetadata` | Tiingo, TwelveData |
| `OptionChainRequest` | `OptionQuote` | — no shipped adapter yet |

`DividendHistoryRequest` returns per-ex-date cash distributions; deriving a trailing income yield from them is consumer-side portfolio analytics, not kernel work — see the [dividend history guide](https://github.com/boutquin/Boutquin.MarketData/blob/main/docs/dividends.md) for the ownership boundary, a worked trailing-12-month example, and the normalizer/cache/empty-window rules that go with it.

## Canonical Records

| Record | Description |
|--------|-------------|
| `Bar` | OHLCAV price bar for equities and ETFs |
| `FxPair` / `FxRate` | Currency pair identifier and FX spot rate observation |
| `ScalarObservation` | Single-value time series (economic indicators, fixings) |
| `YieldCurveQuote` | Yield curve node (tenor + rate) |
| `FuturesSettlement` | Futures settlement price with typed product code and contract month |
| `InstrumentMetadata` | Reference metadata: symbol, exchange, asset class, symbol standard |
| `DividendPayment` | Ex-date, amount, currency, and `DividendType` classification |
| `CorporateAction` | Generic corporate action record |
| `OptionQuote` | Option contract quote |
| `FactorObservation` | Factor data series observation |

## Getting Started

Add a reference to the abstractions package:

```bash
dotnet add package Boutquin.MarketData.Abstractions --version 2.0.0-rc.20261027.1
```

To use the full pipeline with DI composition:

```bash
dotnet add package Boutquin.MarketData.DependencyInjection --version 2.0.0-rc.20261027.1
```

Register the kernel in your `IServiceCollection`:

```csharp
services.AddMarketDataKernel(
    configurePipeline: options =>
    {
        options.L1CacheTtl = TimeSpan.FromMinutes(5);
        options.ContinueOnPartitionFailure = true;
    },
    configureTransport: transport =>
    {
        transport.MaxRetryAttempts = 3;
    });
```

`AddMarketDataKernel` also accepts optional directory overrides for the snapshot store, raw-document store, checkpoint store, and event log (defaults live under `AppContext.BaseDirectory`).

Register adapters via their own package-specific extensions from [Boutquin.MarketData.Adapter](https://github.com/boutquin/Boutquin.MarketData.Adapter) — for example `services.AddMarketDataTiingo(opts => opts.ApiToken = "...")`, `services.AddMarketDataFrankfurter()`, `services.AddMarketDataFred(opts => opts.ApiKey = "...")`. To register your own adapter, implement `IDataSourceAdapter<TRequest, TRecord>` (optionally `IPrioritizedAdapter` for fallback ordering) and add it to DI with `TryAddEnumerable` so multiple adapters for the same `<TRequest, TRecord>` pair can coexist — the kernel's `FallbackOrchestrator` walks them in priority order.

## Building

```bash
dotnet build Boutquin.MarketData.slnx --configuration Release
dotnet test Boutquin.MarketData.slnx --configuration Release
dotnet format Boutquin.MarketData.slnx --verify-no-changes
```

## Documentation

- [Architecture Overview](docs/architecture.md) — layered package design, pipeline internals, and three-layer data architecture constraint
- [Time-Series Store Cache Tier](docs/time-series-cache.md) — the preview `UseTimeSeriesStore` gap-fill flow, snapshot-isolated reads, segment-format and compaction choices, quarantine options, and the Boutquin.Storage 2.0 wire-format migration note
- [Default Normalizer Registration](docs/normalization.md) — what the nine shipped normalizers do, the opt-in `RegisterDefaultNormalizers` flag, host-override and opt-out recipes, the `InstrumentMetadata` asymmetry, and the cache-coherence rule for enabling the flag over an existing cache directory
- [Reference Data](docs/reference-data.md) — Tier 1 and Tier 2 enumerations, `Symbol`, `ExchangeMetadata` / `CountryMetadata`, rate benchmarks, instrument conventions, fixings, exception hierarchy, `Provenance` / `Diagnostics` typed wrappers, external identifiers, and the pre-typed migration table
- [Day-Count Conventions](docs/day-counts.md) — shipped conventions, alias table, `ActualActualInPeriod` ICMA guidance, and the `Thirty360` Bond Basis vs. 30E/360 Eurobond distinction

## Disclaimer

Boutquin.MarketData is open-source software provided under the Apache 2.0 License. It is a general-purpose library intended for educational and research purposes.

**This software does not constitute financial advice.** The data ingestion, normalization, and pipeline tools are provided as-is for research and development. Before using any market data or derived calculations in production, consult with qualified professionals who understand your specific requirements and regulatory obligations.

## License

Licensed under the [Apache License, Version 2.0](LICENSE.txt).

Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
