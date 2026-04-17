# Boutquin.MarketData

[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE.txt)
[![Build](https://github.com/boutquin/Boutquin.MarketData.Dev/actions/workflows/pr-verify.yml/badge.svg)](https://github.com/boutquin/Boutquin.MarketData.Dev/actions/workflows/pr-verify.yml)

Shared data ingestion kernel for [Boutquin.Analytics](https://github.com/boutquin/Boutquin.Analytics) and [Boutquin.Trading](https://github.com/boutquin/Boutquin.Trading). Owns transport, caching, storage, normalization, pipeline orchestration, calendars, day-count conventions, and all canonical record and reference-data types — domain repositories own their builders and recipes that compose kernel adapters into domain-specific bundles.

Concrete source adapters (Tiingo, Frankfurter, FRED, Bank of Canada, etc.) live in [Boutquin.MarketData.Adapter](https://github.com/boutquin/Boutquin.MarketData.Adapter). The kernel ships contracts and infrastructure only — adapters are plug-ins, not dependencies.

## Solution Structure

| Project | Description |
|---------|-------------|
| **Boutquin.MarketData.Abstractions** | Core contracts, canonical records, typed request/response models, reference-data taxonomies, `Provenance`, `Diagnostics`, external identifiers, exception hierarchy |
| **Boutquin.MarketData.Transport** | HTTP client abstraction with Polly v8 resilience, SSRF defense, response-size caps, and secret redaction in debug logs |
| **Boutquin.MarketData.Cache** | L1 in-memory cache with TTL, exact-once semantics, and `RetrievalMode.Cache` provenance tagging |
| **Boutquin.MarketData.Storage** | L2 durable snapshot store with atomic writes, `InMemoryFixingsStore`, and streaming-cap DoS defense |
| **Boutquin.MarketData.Catalog** | Provider and dataset registry — adapter registration, priority ordering, and key lookup |
| **Boutquin.MarketData.Calendars** | Business-day calendars: USNY, GBLO, TARGET, CATO, XCME, USSF, CAST; ZURH and TKYO via weekend-only fallback |
| **Boutquin.MarketData.DayCount** | Day-count conventions: ACT/360, ACT/365F, ACT/ACT (ICMA in-period), 30/360 Bond Basis (ISDA 2006 §4.16(f)), 30E/360 Eurobond (ISDA 2006 §4.16(g)) |
| **Boutquin.MarketData.Conventions** | ISDA-style instrument conventions: `OisConvention`, `FraConvention`, `SwapLegConvention`, `FutureContractConvention`, `SettlementConvention`, `InstrumentConventionRegistry` |
| **Boutquin.MarketData.Normalization** | Per-record normalizers: percent-to-decimal, date alignment, split handling, duplicate deduplication |
| **Boutquin.MarketData.Orchestration** | `DataPipeline` — L1 cache → source selection → fetch → normalize → L2 snapshot → L1 promote |
| **Boutquin.MarketData.DependencyInjection** | `IServiceCollection` composition helpers; `AddMarketDataKernel` wires all ten record-type caches, the pipeline, and the transport layer |
| **Boutquin.MarketData.Testing** | `EnvelopeBuilder`, `FakeDataSourceAdapter`, `FakeHttpMessageHandler`, and fixture helpers for test isolation |

**Test projects** — 14 total: one unit-test project per source package, `Boutquin.MarketData.Tests.Integration` (full pipeline round-trips), and `Boutquin.MarketData.ArchitectureTests` (zero-domain-dependency and file-system-access enforcement).

## Architecture

```
Boutquin.MarketData.Abstractions      Core contracts + canonical records (zero dependencies)
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

**Key constraint:** MarketData must not reference Analytics, Trading, or OptionPricing. It is the shared kernel that all three consume.

## Reference Data

### Tier 1 — Core Enumerations (`Abstractions/ReferenceData/`)

Canonical market identifiers shared across the entire Boutquin ecosystem. Numeric values are stable — persisted records and network formats depend on them.

| Type | Standard | Members |
|------|----------|---------|
| `CurrencyCode` | ISO 4217 (numeric values) | 9 currencies (USD=840, EUR=978, …) |
| `ExchangeCode` | ISO 10383 MIC | XNYS, XNAS, XLON, XPAR, XTSE, XCME, … |
| `AssetClassCode` | — | Equity, FixedIncome, FX, Commodity, Derivative, … |
| `SecuritySymbolStandard` | — | Ticker, ISIN, CUSIP, SEDOL, FIGI, … |
| `Symbol` | — | Readonly record struct; null/whitespace-rejecting; ordinal ticker ordering |

### Tier 2 — Market Structure Taxonomies

| Type | Standard | Members |
|------|----------|---------|
| `CountryCode` | ISO 3166-1 alpha-2 | 11 members |
| `ContinentCode` | UN geoscheme | 7 members |
| `TimeZoneCode` | ISO 8601 | 9 standard-time designators |
| `DividendType` | — | 8 issuer-declared categories; no `Unknown` sentinel |

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

`DataPipeline.FetchAsync<TRequest, TRecord>` executes the canonical kernel path:

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
dotnet add package Boutquin.MarketData.Abstractions
```

To use the full pipeline with DI composition:

```bash
dotnet add package Boutquin.MarketData.DependencyInjection
```

Register the kernel in your `IServiceCollection`:

```csharp
services.AddMarketDataKernel(options =>
{
    options.L1CacheTtl = TimeSpan.FromMinutes(5);
    options.ContinueOnPartitionFailure = true;
});
```

Implement `IDataSourceAdapter<TRequest, TRecord>` for your data source (or take a pre-built adapter from `Boutquin.MarketData.Adapter`), then register it with the pipeline via `AddMarketDataAdapter<TRequest, TRecord, TAdapter>`.

## Building

```bash
dotnet build Boutquin.MarketData.slnx --configuration Release
dotnet test Boutquin.MarketData.slnx --configuration Release
dotnet format Boutquin.MarketData.slnx --verify-no-changes
```

## Documentation

- [Architecture Overview](docs/architecture.md) — layered package design, pipeline internals, and three-layer data architecture constraint
- [Reference Data](docs/reference-data.md) — Tier 1 and Tier 2 enumerations, `Symbol`, `ExchangeMetadata` / `CountryMetadata`, rate benchmarks, instrument conventions, fixings, exception hierarchy, `Provenance` / `Diagnostics` typed wrappers, external identifiers, and the pre-typed migration table
- [Day-Count Conventions](docs/day-counts.md) — shipped conventions, alias table, `ActualActualInPeriod` ICMA guidance, and the `Thirty360` Bond Basis vs. 30E/360 Eurobond distinction

## Disclaimer

Boutquin.MarketData is open-source software provided under the Apache 2.0 License. It is a general-purpose library intended for educational and research purposes.

**This software does not constitute financial advice.** The data ingestion, normalization, and pipeline tools are provided as-is for research and development. Before using any market data or derived calculations in production, consult with qualified professionals who understand your specific requirements and regulatory obligations.

## License

Licensed under the [Apache License, Version 2.0](LICENSE.txt).

Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
