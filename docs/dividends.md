# Dividend History and Trailing Income Yield

`IDataPipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>` returns the
cash distributions a symbol paid over a date range — one `DividendPayment` per
ex-date on which cash was actually distributed. This page documents what the
kernel returns, what it deliberately does **not** compute, and the three
behaviours a consumer has to know about before wiring a trailing income yield on
top: normalizer registration, exact-range cache keys, and the empty-window case.

## Ownership boundary — the kernel returns records, the consumer derives the yield

A trailing-12-month income yield is

```
sum(DividendPayment.Amount where ExDate in (asOf - 365d, asOf]) / price(asOf)
```

Every term on the right of that expression except the dividend amounts belongs
to the consumer: the as-of price comes from a *different* fetch, and the window
length, the annualization convention, and the split-adjustment policy are all
portfolio-analytics decisions. MarketData describes what the market *is*; it
does not decide what a strategy does with it.

So the boundary is drawn here, deliberately:

- **The kernel returns `DividendPayment` records** for a `(symbol, range)` —
  ex-date, amount, currency, and a `DividendType` classification. A default
  host preserves the adapter's raw amount; clamping and rounding happen only
  when the host opts into or explicitly registers `DividendPaymentNormalizer`.
  That is where the kernel's responsibility ends.
- **The consumer sums and divides.** The trailing-yield derivation lives in the
  consuming application (a portfolio pipeline, a signal library), alongside the
  price series it needs anyway.

**There is no `GetTrailingYieldAsync` in this kernel, and no yield calculator
belongs in MarketData.** A future contributor who needs one should add it to the
consuming application, not push it down into the data kernel — pushing it down
would force the kernel to take a position on as-of pricing and annualization
that it has no business taking.

## Fetching dividend history

```csharp
var request = new DividendHistoryRequest(
    [new Symbol("SCHD")],
    new DateRange(new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30)));

DataEnvelope<IReadOnlyList<DividendPayment>> envelope =
    await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request, ct);

foreach (var payment in envelope.Payload)
{
    Console.WriteLine($"{payment.ExDate:yyyy-MM-dd}  {payment.Amount} {payment.Currency}");
}
```

The request is a batch request over `Symbols`. On the time-series path, the
pipeline splits a multi-symbol request per symbol before constructing each
per-symbol dataset key. The default legacy L1/L2 cache instead keys the
original aggregate request before dispatch; replaying that same batch reuses
its aggregate cache entry.
Distributions are an *event* series: a full year's request typically returns
four records, not 252. That is the expected shape, not a coverage gap.

On the amounts' classification: the shipped Tiingo source reports a single
blended cash figure per ex-date, which for an ETF routinely mixes ordinary
income, return of capital, and capital-gain components. Those records therefore
carry `DividendType.Unclassified` with the inference logged, rather than being
stamped with a jurisdictional category that would be wrong for part of the
blend. A consumer that needs component-level tax treatment must classify from a
source that actually supplies it.

## Registering the normalizer

`DataPipeline` resolves `INormalizer<TRecord, TRecord>` **optionally** and skips
normalization when none resolves. `AddMarketDataKernel` does register a
`DividendPaymentNormalizer` — but that registration is a resolution-time factory
gated on `DataPipelineOptions.RegisterDefaultNormalizers`, and that flag is
declared without an initializer, so it is **`false` by default** and the
registration resolves `null`. The practical consequence is what matters:

> In a default host, no normalizer runs on `DividendPayment`, and raw adapter
> amounts reach the caller un-clamped and un-rounded.

Pick one of the two supported ways to turn it on:

```csharp
// (a) opt into the kernel's own nine normalizers, including DividendPayment
services.AddMarketDataKernel(configurePipeline: o => o.RegisterDefaultNormalizers = true);

// (b) or register just this one yourself — a host registration always wins
services.AddSingleton<INormalizer<DividendPayment, DividendPayment>>(
    new DividendPaymentNormalizer());
```

`DividendPaymentNormalizer` clamps negative amounts to zero and rounds to a
configurable precision (4 decimal places by default, `MidpointRounding.AwayFromZero`).
Resolve normalizers with `GetService`, never `GetRequiredService` — with the
flag off the kernel registration deliberately resolves `null`.

## Cache keys are exact-range

The L2 snapshot tier keys each entry on the **exact** requested `(From, To)`
range and serves only exact-key matches. A dividend fetch therefore reuses a
cached window only when it is issued with the *same* range coordinates as the
fetch that populated it.

Repeating the same dividend request with the same range coordinates can reuse
that dividend entry; narrowing its range by a day is a cache miss, not a subset
read. A price request always occupies a separate record-type cache identity, so
its range coordinates neither enable nor prevent dividend-cache reuse. Align the
two ranges when the yield calculation needs the same analytical window, not to
make their cache keys match.

## A window with no ex-dates is an empty success

Most symbols pay nothing in most windows, and a non-payer pays nothing ever.
That case is a legitimate answer, not a failed fetch.

The kernel registers a `DividendPayment`-specific response evaluator for exactly
this reason. When an adapter reaches the provider, parses the window, and finds
no ex-dates — an empty payload carrying `Complete` coverage for the requested window —
the response is accepted as an **empty success**: `envelope.Payload` is empty and
no error-severity issue is raised. Without it, the pipeline's default evaluator
rejects any empty payload before it examines coverage, and the ordinary
non-payer surfaces as `Missing` coverage plus an error-severity `PARTITION_FAILED`
issue — indistinguishable from a provider outage.

So a consumer computing trailing yield across a universe should treat an empty
`Payload` with `Complete` coverage and no error-severity issue as *"this symbol
distributed nothing in this window"* — yield `0`, not a data error. An empty
payload with `Partial` or `Missing` coverage does not establish zero yield;
inspect its coverage and issues before using it. Genuinely failed fetches still
surface as `PARTITION_FAILED`, so the two remain distinguishable.

## Worked example — trailing-12-month yield

As-of date **2026-06-30**, symbol **SCHD**, trailing window
**(2025-06-30, 2026-06-30]** — half-open, so a payment whose ex-date falls *on*
the opening bound belongs to the previous window.

Records returned for a request covering that window:

| Ex-date | Amount (USD) | Window |
|---|---|---|
| 2025-06-24 | 0.2544 | out — ex-date precedes the window's open bound |
| 2025-09-24 | 0.2645 | in |
| 2025-12-10 | 0.2712 | in |
| 2026-03-25 | 0.2489 | in |
| 2026-06-24 | 0.2803 | in |

**Trailing-12M cash** = 0.2645 + 0.2712 + 0.2489 + 0.2803 = **1.0649**

**Price on 2026-06-30** = **27.50**

**Trailing-12M yield** = 1.0649 / 27.50 = 0.038724 → **3.87 %**

```csharp
var asOf = new DateOnly(2026, 6, 30);
var windowOpen = asOf.AddDays(-365);

var trailingCash = envelope.Payload
    .Where(p => p.ExDate > windowOpen && p.ExDate <= asOf)
    .Sum(p => p.Amount);

if (priceAsOf <= 0m)
{
    throw new InvalidOperationException("A positive as-of price is required to calculate yield.");
}

var yield = trailingCash / priceAsOf;
```

Three details the example is chosen to make explicit:

1. **The window is half-open.** The 2025-06-24 payment is excluded; including it
   would double-count it in the neighbouring window and overstate the yield by
   roughly a quarter's distribution.
2. **The price is the consumer's.** It comes from a `PriceHistoryRequest`, not
   from this path — which is precisely why the derivation cannot live in the kernel.
3. **An empty `Payload` yields `0` only when a valid positive price is available.**
   A missing or non-positive as-of price makes the yield unavailable; reject or
   propagate that missing observation rather than fabricating a market value of zero.

## Related

- [Default Normalizer Registration](normalization.md) — what each of the nine
  shipped normalizers does, the opt-in flag, and per-record override/opt-out recipes.
- [Time-Series Store Cache Tier](time-series-cache.md) — the opt-in gap-fill
  cache path and its per-dataset-key coverage model.
- [Architecture Overview](architecture.md) — where the pipeline's normalize and
  evaluate steps sit in the fetch path.
