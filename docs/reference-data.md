# Reference data — benchmarks, conventions, taxonomies, and fixings

> **Status:** shipped in the reference-data consolidation release. See
> `specs/feature-reference-data-and-daycounts.md` (private Dev repo) for the
> motivation, acceptance criteria, and out-of-scope list.

Boutquin.MarketData is the shared data ingestion kernel for
Boutquin.Analytics, Boutquin.Trading, and Boutquin.OptionPricing. This guide
walks through the four reference-data surfaces that describe *what the market
is* — independent of any single consumer's domain logic.

## 1. Tier 1 reference-data enums

All four are closed sets maintained by external standards bodies, so compile-time
exhaustiveness on switch expressions is intentional and desirable. They live in
`Boutquin.MarketData.Abstractions.ReferenceData`.

| Enum | Standard | Example members |
|------|----------|-----------------|
| `CurrencyCode` | ISO 4217 (numeric ordinals pinned — `USD = 840`, `EUR = 978`) | USD, CAD, GBP, EUR, JPY, CHF … |
| `ExchangeCode` | ISO 10383 MIC | XNYS, XNAS, XLON, XPAR, XTSE, … |
| `AssetClassCode` | Internal taxonomy | CashAndCashEquivalents, FixedIncome, Equities, Commodities, … |
| `SecuritySymbolStandard` | Internal taxonomy | Cusip, Isin, Sedol, Ric, BloombergTicker |

### Drift policy

These enums are mirrored on the Trading side during the migration window
(see `Boutquin.Trading.Domain.Enums`). Adding a new member requires **lockstep**
updates in both repos — the acceptance tests pin every numeric value with a
snapshot (`CurrencyCodeTests.ISO_4217_numeric_values_are_pinned`) so any drift
fails CI. Removals must go through a coordinated deprecation spec.

## 1a. Tier 2 reference-data taxonomies

Four additional taxonomies describe **what the market is** rather than
**what strategies do** — jurisdiction, geography, timezone, and income
classification. They live in the same `Boutquin.MarketData.Abstractions.ReferenceData`
namespace and follow the same drift policy as Tier 1.

| Enum | Standard | Example members |
|------|----------|-----------------|
| `CountryCode` | ISO 3166-1 alpha-2 | CA, US, GB, JP, DE, … |
| `ContinentCode` | UN geoscheme groups | AF, AN, AS, EU, NA, OC, SA |
| `TimeZoneCode` | ISO 8601 time-zone designators | UTC, CET, GMT, EST, JST, HKT, MSK, AEST, CST |
| `DividendType` | Issuer-declared categories | CanadianEligible, UsQualified, UsOrdinary, Foreign, ReturnOfCapital, … |

### Exchange + country metadata lookups

`ExchangeCode` composes with the Tier 2 taxonomies via two static lookups:

```csharp
using Boutquin.MarketData.Abstractions.ReferenceData;

ExchangeMetadata.GetTimeZone(ExchangeCode.XNYS);  // TimeZoneCode.EST
ExchangeMetadata.GetCountry(ExchangeCode.XTSE);   // CountryCode.JP
CountryMetadata.GetContinent(CountryCode.JP);     // ContinentCode.AS
```

The tables are type-initialised and exhaustive over their enum domain — every
declared `ExchangeCode` member has a timezone and country mapping, and every
declared `CountryCode` member has a continent mapping. Passing a value outside
the declared set (for example, `(ExchangeCode)(-1)`) throws
`ArgumentOutOfRangeException` at call time so missing mappings surface
immediately rather than silently degrading. When adding a new enum member,
add its mapping in the same commit — there is intentionally no runtime
`Register` / `Set` override.

The returned `TimeZoneCode` is the **standard-time** designation for the
exchange (XNYS returns `EST` year-round, not `EDT` in summer). Consumers that
need DST-aware session hours should resolve through `TimeZoneInfo` or an
equivalent IANA-aware library — the enum classifies the venue, it does not
play the role of a clock.

### `IDividendClassifier`

Classifying a raw dividend into a jurisdictional category is a data-layer
concern — the classifier annotates the observation before any consumer (tax
engine, portfolio-income aggregator, reporting pipeline) reads it. The
interface lives at `Boutquin.MarketData.Abstractions.IDividendClassifier`:

```csharp
using Boutquin.MarketData.Abstractions;
using Boutquin.MarketData.Abstractions.ReferenceData;

IDividendClassifier classifier = /* your implementation */;
var type = classifier.Classify(new Symbol("AAPL"), CurrencyCode.USD, CurrencyCode.CAD);
// => DividendType.Foreign (asset in USD, investor base in CAD)
```

Implementations must honour the **unknown-input contract**:

| Case | Behaviour |
|------|-----------|
| Precise match in metadata tables | Return the specific `DividendType` (e.g., `CanadianEligible`, `UsQualified`, `Reit`) |
| Asset currency ≠ investor base currency, no precise match | Return `DividendType.Foreign` |
| Asset currency = investor base currency (USD), no precise match | Return `DividendType.UsOrdinary` |
| Asset currency = investor base currency (CAD), no precise match | Return `DividendType.CanadianNonEligible` |
| Any well-formed input | Never throws, never returns `default(DividendType)` |
| `default(Symbol)` | Implementations MAY throw `ArgumentException` — `Symbol`'s ctor rejects null/whitespace, so this only arises from explicit misuse |

The classifier never reports "unclassifiable". There is no `Unknown` sentinel
on `DividendType` — pushing ambiguity downstream produces exactly the
inference burden the typed field removes.

### `DividendPayment.Type`

`DividendPayment` carries the issuer-declared classification as a first-class
field:

```csharp
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;

var dividend = new DividendPayment(
    new DateOnly(2024, 8, 15),
    Amount: 0.82m,
    Currency: CurrencyCode.USD,
    Type: DividendType.UsQualified);
```

Adapters emit the `Type` directly when the upstream data source supplies it,
or use an `IDividendClassifier` to enrich the payment downstream (the record
supports `with { Type = ... }` for the enrich-after-construction workflow).
Normalisers pass `Type` through unchanged — classification is not a unit or
format concern.

## 2. `Symbol` and `ISecurityResolver`

```csharp
using Boutquin.MarketData.Abstractions;
using Boutquin.MarketData.Abstractions.ReferenceData;

var aapl = new Symbol("AAPL");                // throws on null/whitespace
var ryNyse = new Symbol("RY");
var ryTsx  = new Symbol("RY.TO");

ISecurityResolver resolver = /* your dual-listing table */;
resolver.ResolvePrimary(ryTsx);   // returns ry on NYSE
resolver.AreEquivalent(ryNyse, ryTsx); // true
resolver.AllListings(ryNyse);     // { RY, RY.TO }
resolver.ResolvePrimary(aapl);    // identity — returns aapl unchanged
```

`Symbol` is a `readonly record struct`. Case is preserved (`"aapl"` and
`"AAPL"` are distinct symbols because some venues distinguish ticker casing).
The constructor rejects `null`, empty, or whitespace tickers.

`ISecurityResolver` guarantees three invariants on every well-formed input:

| Method | Unknown-symbol behaviour |
|--------|--------------------------|
| `ResolvePrimary` | Returns the input unchanged (never throws, never returns `default`) |
| `AreEquivalent` | Reflexive; commutative; two unknown symbols are equivalent iff structurally equal |
| `AllListings` | Non-null, non-empty; always contains the input itself |

`Bar` intentionally does not carry a `Symbol` field — request objects and
pipeline envelopes do. Key external collections explicitly:
`Dictionary<Symbol, IReadOnlyList<Bar>>`.

## 3. Rate benchmarks

```csharp
using Boutquin.MarketData.Abstractions.ReferenceData;

var sofr  = StandardBenchmarks.UsdSofr;
var saron = StandardBenchmarks.ChfSaron;

Console.WriteLine(sofr.Name.Value);       // "USD-SOFR"
Console.WriteLine(sofr.Currency);          // CurrencyCode.USD
Console.WriteLine(sofr.Kind);              // BenchmarkKind.OvernightRiskFree
Console.WriteLine(sofr.CalendarCode);      // "USNY"
Console.WriteLine(sofr.DayCountCode);      // "ACT/360"

StandardBenchmarks.All.Count;              // 6
StandardBenchmarks.ByName("usd-sofr");     // case-insensitive; returns sofr
StandardBenchmarks.ByName("USDSOFR");      // null — aliases not supported in v1
```

The catalogue ships six overnight benchmarks — the ones Hull 11e Ch 4 names
explicitly as the post-LIBOR reference rate set:

| Property | USD SOFR | GBP SONIA | EUR ESTR | CAD CORRA | CHF SARON | JPY TONA |
|----------|----------|-----------|----------|-----------|-----------|----------|
| Currency | USD | GBP | EUR | CAD | CHF | JPY |
| Kind | OvernightRiskFree | OvernightRiskFree | OvernightRiskFree | OvernightIndexed | OvernightIndexed | OvernightIndexed |
| Calendar | USNY | GBLO | TARGET | CATO | ZURH | TKYO |
| Day count | ACT/360 | ACT/365F | ACT/360 | ACT/365F | ACT/360 | ACT/365F |
| Observation lag (days) | 0 | 0 | 0 | 0 | 0 | 0 |

`CalendarCode` and `DayCountCode` are free-form `string` — the catalogue is an
open set that grows as new venues or conventions arrive. Resolve them with
`HolidayCalendarFactory.Create(benchmark.CalendarCode)` and
`DayCountResolver.Resolve(benchmark.DayCountCode)`.

`StandardBenchmarks.All` returns the same `IReadOnlyList<RateBenchmark>`
instance on every call; callers may compare by reference.

## 4. Instrument conventions

Instrument-level structural records — OIS / FRA / swap-leg / futures /
settlement — live in `Boutquin.MarketData.Conventions`. They were relocated
verbatim from `Boutquin.Curves.Conventions` so consumers that previously took
a Curves reference for structural conventions can drop that dependency.

```csharp
using Boutquin.MarketData.Abstractions.Calendars;
using Boutquin.MarketData.Conventions;

var registry = InstrumentConventionRegistry.CreateDefault();

// Pre-populated with four-currency OIS (USD / GBP / EUR / CAD), USD FRA,
// USD SR3 future, and three swap legs.
var usdOis = (OisConvention)registry.GetRequired("USD-SOFR-OIS");

usdOis.CalendarCode;                  // "USNY"
usdOis.DayCountCode;                  // "ACT/360"
usdOis.PaymentFrequency;              // PaymentFrequency.Annual
usdOis.BusinessDayAdjustment;         // BusinessDayAdjustment.ModifiedFollowing
usdOis.SettlementLagBusinessDays;     // 2
usdOis.ObservationShiftConvention;    // ObservationShiftConvention.Shifted
```

### Registry semantics

- **Add is last-write-wins.** Registering a convention whose `Code` already
  exists silently overwrites the previous entry. Callers that need "add once"
  semantics must guard with `GetRequired` first.
- **Lookup is case-insensitive.** `"usd-sofr-ois"` and `"USD-SOFR-OIS"` hit the
  same entry.
- **`CreateDefault()` returns a fresh instance on every call.** Mutating a
  default registry does not leak into other callers.
- **`GetRequired(code)` throws `KeyNotFoundException`** (not a bespoke
  `MarketDataException`) when the key is absent. A non-throwing `TryGet`
  overload is intentionally out of scope for v1; add it when a consumer
  surfaces the need.

### Supporting enums

`PaymentFrequency`, `RollConvention`, `StubConvention`, and
`ObservationShiftConvention` are all relocated verbatim from Curves with
ordinals preserved — safe to `(int)` cast across the two enums during the
migration window.

## 5. Fixings

Fixings — the realised values of a benchmark on a given date — have a
read-only contract (v1):

```csharp
using Boutquin.MarketData.Abstractions;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Storage.InMemory;

IFixingsStore store = new InMemoryFixingsStore(
[
    (new BenchmarkName("USD-SOFR"), new DateOnly(2026, 1, 2), 0.0533m),
    (new BenchmarkName("USD-SOFR"), new DateOnly(2026, 1, 3), 0.0534m),
]);

store.GetFixing(new BenchmarkName("USD-SOFR"), new DateOnly(2026, 1, 2));
// => 0.0533m

store.TryGetFixing(new BenchmarkName("USD-SOFR"), new DateOnly(2026, 1, 2), out var v);
// => true, v = 0.0533m

var range = new DateRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 7));
var series = store.GetFixings(new BenchmarkName("USD-SOFR"), range);
// => ordered ascending, inclusive at both ends
```

`InMemoryFixingsStore` is suitable for tests and single-process workflows. A
storage-backed implementation will arrive in a future spec — the write-side
interface is intentionally deferred until that lands.

Duplicate `(benchmark, date)` tuples in the constructor seed follow a
last-in-wins rule silently. Callers that need strict uniqueness must validate
before construction.

## 6. Error handling

Every MarketData failure surfaces as a `Boutquin.MarketData.Abstractions.Exceptions.MarketDataException`:

```csharp
try
{
    var bars = await pipeline.FetchAsync(priceRequest, ct);
}
catch (MarketDataNotFoundException) { /* provider returned empty */ }
catch (MarketDataRetrievalException) { /* transport error */ }
catch (MarketDataProcessingException) { /* normalizer rejection */ }
catch (MarketDataStorageException) { /* snapshot persistence failed */ }
catch (MarketDataException) { /* catch-all for the family */ }
```

`MarketDataException` is abstract; throw one of the five concrete subclasses.
`SymbolNotFoundException` covers symbol-resolution failures specifically — it
used to be called `SymbolReaderException` in the Trading-hosted hierarchy.

## 6a. Provenance and diagnostic types

Provenance (`DataProvenance`) and diagnostics (`DataIssue`) were previously
serialized with a handful of bare `string` fields whose values had closed-ish
sets but no type to enforce them. Two new sub-namespaces address that:

### `Boutquin.MarketData.Abstractions.Provenance`

```csharp
public readonly record struct ProviderCode(string Value);    // normalises to lower-case
public enum LicenseType { Free, DelayedFifteenMin, Commercial, ResearchOnly, Redistribution }
public enum RetrievalMode { Api, Cache, Snapshot, Derived, Fixture }
```

`ProviderCode` is constructed by each adapter package as a
`public static readonly` field (the canonical spelling) and returned from
`IDataSourceAdapter<TRequest, TRecord>.ProviderKey`. Case is normalised at
construction, so `new ProviderCode("Tiingo")` and `new ProviderCode("TIINGO")`
are equal. `LicenseType` and `RetrievalMode` are closed enums — new members are
MarketData changes, not adapter changes, and consumers can exhaustively `switch`
on them under `TreatWarningsAsErrors=true`.

### `Boutquin.MarketData.Abstractions.Diagnostics`

```csharp
public enum IssueSeverity { Warning, Error }
public readonly record struct IssueCode(string Value);       // normalises to upper-case
// well-known statics (canonical spelling):
IssueCode.MissingField, IssueCode.StaleData, IssueCode.RateLimited,
IssueCode.HolidayGap, IssueCode.UnexpectedGap, IssueCode.SchemaDrift,
IssueCode.SnapshotStale, IssueCode.PartitionFailed
```

`IssueCode` has a long tail — adapters introduce new codes as they encounter new
failure modes. Ad-hoc construction is allowed (`new IssueCode("TIINGO_QUOTA_EXCEEDED")`)
and the well-known statics are the vocabulary MarketData itself emits from its
coverage classifier, snapshot staleness policy, and pipeline partition handler.

### `Boutquin.MarketData.Abstractions.ReferenceData` — external-identifier wrappers

```csharp
public readonly record struct YieldCurveId(string Value);       // e.g. "CAD-ZERO"
public readonly record struct EconomicSeriesId(string Value);   // e.g. "DGS10"
public readonly record struct FactorDatasetId(string Value);    // e.g. "F-F_Research_Data_Factors_daily"
public readonly record struct FuturesProductCode(string Value); // e.g. "SR3"

public readonly record struct ContractMonth            // canonical "YYYY-MM" rendering
{
    public int Year { get; }
    public int Month { get; }                          // 1–12
    public ContractMonth(int year, int month);
    public ContractMonth(string text);                 // accepts "YYYY-MM" or "YYYYMM"
    public static implicit operator DateOnly(ContractMonth m); // first-of-month
}
```

These five replace bare `string` fields on `YieldCurveQuoteRequest.CurveId`,
`EconomicSeriesRequest.SeriesId`, `FactorSeriesRequest.DatasetName`,
`FuturesSettlementRequest.ProductCode`, `FuturesSettlement.ContractCode`, and
`FuturesSettlement.ContractMonth`. All follow the `Symbol` pattern — thin
wrappers around `string` with null/whitespace rejection — except `ContractMonth`,
which carries a `(Year, Month)` tuple because futures delivery months have
structured calendar semantics.

### Migration mapping (release notes)

| Record / Request | Field | Old shape (string) | New shape |
|------------------|-------|--------------------|-----------|
| `DataProvenance` | `Provider` | `"tiingo"` | `new ProviderCode("tiingo")` |
| `DataProvenance` | `LicenseFlag` | `"free"` | `LicenseType.Free` |
| `DataProvenance` | `LicenseFlag` | `"delayed-15m"` | `LicenseType.DelayedFifteenMin` |
| `DataProvenance` | `LicenseFlag` | `"commercial"` | `LicenseType.Commercial` |
| `DataProvenance` | `RetrievalMode` | `"api"` | `RetrievalMode.Api` |
| `DataProvenance` | `RetrievalMode` | `"cache"` | `RetrievalMode.Cache` |
| `DataProvenance` | `RetrievalMode` | `"snapshot"` | `RetrievalMode.Snapshot` |
| `DataProvenance` | `Dataset` | `"AAPL"` | *(stays `string`)* |
| `DataIssue` | `Code` | `"STALE_DATA"` | `IssueCode.StaleData` (or `new IssueCode("...")`) |
| `DataIssue` | `Severity` | `"Warning"` | `IssueSeverity.Warning` |
| `FallbackResult<T>` | `SelectedProviderKey` | `"tiingo"` | `new ProviderCode("tiingo")` |
| `FallbackAttempt` | `ProviderKey` | `"tiingo"` | `new ProviderCode("tiingo")` |
| `IDataSourceAdapter<…>` | `ProviderKey` | `string` | `ProviderCode` |
| `YieldCurveQuoteRequest` | `CurveId` | `"CAD-ZERO"` | `new YieldCurveId("CAD-ZERO")` |
| `EconomicSeriesRequest` | `SeriesId` | `"DGS10"` | `new EconomicSeriesId("DGS10")` |
| `FactorSeriesRequest` | `DatasetName` | `"F-F_..."` | `new FactorDatasetId("F-F_...")` |
| `FuturesSettlementRequest` | `ProductCode` | `"SR3"` | `new FuturesProductCode("SR3")` |
| `FuturesSettlement` | `ContractCode` | `"SR3"` | `new FuturesProductCode("SR3")` |
| `FuturesSettlement` | `ContractMonth` | `"2025-06"` | `new ContractMonth(2025, 6)` or `new ContractMonth("2025-06")` |

If an external consumer deserialises `DataProvenance` or `DataIssue` from JSON,
note that `ProviderCode` / `IssueCode` / the external-ID record structs now
serialise as `{"Value": "..."}` nested objects, and `LicenseType` /
`RetrievalMode` / `IssueSeverity` serialise as ordinal integers by default. All
record structs support `[JsonConstructor]`-driven round-trip through
`System.Text.Json`.

## 7. Cross-repo coordination

- **Boutquin.Curves** keeps deprecated shims for the `RateBenchmark`,
  fixings, and convention types for one release cycle. The Curves
  modernization spec handles the removal.
- **Boutquin.Trading** keeps `using` aliases for the four Tier 1 enums,
  `Asset`, `ISecurityResolver`, the `MarketData*Exception` types, the four
  Tier 2 taxonomies (`CountryCode`, `ContinentCode`, `TimeZoneCode`,
  `DividendType`), and `IDividendClassifier`. The Trading cleanup spec handles
  the removal.
- **Drift in `CurrencyCode` / `ExchangeCode` / `AssetClassCode` /
  `SecuritySymbolStandard` / `CountryCode` / `ContinentCode` / `TimeZoneCode` /
  `DividendType`** between MarketData and Trading is not a supported state.
  Additions land in both repos in the same release; removals go through a
  coordinated deprecation spec.
- **Boutquin.TaxEngine** consolidates its internal `DividendType` onto the
  MarketData copy. Tax-treatment tables (gross-up factors, DTC eligibility,
  withholding rates) stay in TaxEngine — those are tax rules, not market
  facts. Tracked in `Boutquin.TaxEngine/specs/feature-dividend-income-classification.md`.
