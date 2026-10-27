# Default Normalizer Registration

`DataPipeline` normalizes every record it fetches through a registered
`INormalizer<TRecord, TRecord>` — but the pipeline resolves that normalizer
optionally and skips the step entirely when none is active. `AddMarketDataKernel`
registers resolution-time factories for the shipped normalizers, gated by
`DataPipelineOptions.RegisterDefaultNormalizers`. Because the flag defaults to
`false`, records flow through exactly as the adapter emitted them unless a host
opts in or registers its own normalizer: unrounded prices, negative volumes and
amounts left un-clamped, inconsistent unit strings (`"Percent"` vs `"pct"` vs
`"%"`), and inverted OHLC bars.

`DataPipelineOptions.RegisterDefaultNormalizers` (default **`false`**) closes
that gap. With the flag on, `AddMarketDataKernel` registers all nine shipped
normalizers — one per canonical record type — so the pipeline's normalization
step actually runs. With the flag off (the shipped default), existing
consumers see no behavior change: the registered factories resolve to no normalizer, and the pipeline
passes records through exactly as before.

## What each normalizer does

| Record type | Normalizer | Transformations |
|---|---|---|
| `Bar` | `BarNormalizer` | Rounds OHLC + adjusted close to a configurable precision (default 4 dp, `AwayFromZero`); swaps `High`/`Low` when inverted; clamps negative `Volume` to zero. |
| `CorporateAction` | `CorporateActionNormalizer` | Clamps negative dividend amounts and split factors to non-negative/positive; trims the description. |
| `DividendPayment` | `DividendPaymentNormalizer` | Clamps negative amounts to zero; rounds to a configurable precision (default 4 dp). |
| `FactorObservation` | `FactorObservationNormalizer` | Converts percent-form factor returns to decimal (÷100); maps factor names to canonical casing (`Mkt-RF`, `SMB`, `HML`, `RMW`, `CMA`, `RF`, `Mom`, `WML`); unknown names are trimmed and uppercased. |
| `FuturesSettlement` | `FuturesSettlementNormalizer` | Uppercases contract codes; rounds settlement prices (default 4 dp); for the supported price-quoted rate products `SR3` and `ZQ`, derives a missing `ImpliedRate` as `(100 - SettlePrice) / 100` in **decimal form** (e.g. a settle price of 95 synthesizes `0.05`, not `5`). Other products retain `null`. |
| `FxRate` | `FxRateNormalizer` | Rounds the rate to a configurable precision (default 6 dp). |
| `OptionQuote` | `OptionQuoteNormalizer` | Converts implied volatility values `>= 1` from presumed percent form to decimal; rounds prices (default 4 dp); clamps negative volume/open interest to zero. The IV rule is a representation heuristic: a genuine decimal volatility of 100% or more is indistinguishable and would be divided by 100. |
| `ScalarObservation` | `ScalarObservationNormalizer` | Converts percent-form values (`percent`/`pct`/`%`, case-insensitive) to decimal and rewrites units to `"decimal"`; lowercases other unit strings unchanged otherwise. |
| `YieldCurveQuote` | `YieldCurveQuoteNormalizer` | Uppercases and trims tenor labels; converts percent-form rates (`\|rate\| >= 1`) to decimal. |

All nine are stateless and default-constructible, and are registered as
singletons — safe because none carries mutable per-call state, only
`readonly` fields (or, for `FactorObservationNormalizer`, a read-only static
lookup table).

All nine also share one input policy. A null reference record is rejected as
`ArgumentNullException("raw")`. A null value in a required string member is
rejected as `ArgumentException("raw")`, with the member name in the message,
before string manipulation or key canonicalization begins. Optional strings
remain optional: a null `CorporateAction.Description` passes through as null.
Record types without string members are unaffected by this guard.

`InstrumentMetadata` is the one canonical record type with **no** shipped
normalizer — no normalization semantics (rounding, clamping, unit
conversion) are defined for reference metadata. `GetService<INormalizer<InstrumentMetadata,
InstrumentMetadata>>()` resolves `null` whether the flag is on or off, and the
pipeline's null-tolerant resolution handles that exactly as it does for every
other unregistered normalizer — this is a documented asymmetry, not a gap to
be filled by this flag.

## Turning it on

```csharp
services.AddMarketDataKernel(configurePipeline: options =>
{
    options.RegisterDefaultNormalizers = true;
});
```

**Any options channel works, in any order.** The kernel registers each default
normalizer through a factory that reads `IOptions<DataPipelineOptions>` when
the normalizer is *resolved*, so the flag does not have to be present at the
moment `AddMarketDataKernel` runs. All of these register the nine normalizers:

```csharp
// after the kernel call
services.AddMarketDataKernel();
services.Configure<DataPipelineOptions>(o => o.RegisterDefaultNormalizers = true);

// through PostConfigure, in either order
services.PostConfigure<DataPipelineOptions>(o => o.RegisterDefaultNormalizers = true);

// through an IConfiguration section, or a typed IConfigureOptions
services.Configure<DataPipelineOptions>(configuration.GetSection("MarketData:Pipeline"));
services.ConfigureOptions<MyPipelineOptionsConfigurer>();
```

The value `IOptions<DataPipelineOptions>` reports and the set of normalizers
the container resolves therefore always describe the same configuration.

## Overriding or opting out per record type

The flag uses ordinary DI semantics — no bespoke override API:

- **A host registration made *before* `AddMarketDataKernel` wins.** The kernel
  registers with `TryAddSingleton`, which yields to an existing registration.
- **A host registration made *after* `AddMarketDataKernel` also wins.**
  Resolution (`GetService<INormalizer<T, T>>()`) returns the *last*
  registration for a service type, so a post-registration replaces the
  kernel's default.
- **Opting a single record type out entirely** (leaving every other type
  normalized): call `services.RemoveAll<INormalizer<TRecord, TRecord>>()`
  after `AddMarketDataKernel`. For example, to leave `Bar` un-normalized
  while every other registered type still normalizes:

  ```csharp
  services.AddMarketDataKernel(configurePipeline: o => o.RegisterDefaultNormalizers = true);
  services.RemoveAll<INormalizer<Bar, Bar>>();
  ```

Calling `AddMarketDataKernel` more than once on the same `IServiceCollection`
(a pattern some test fixtures use to compose multiple host builders) does not
double-register — the kernel's registrations are idempotent per record type
regardless of how many times the method runs. Options delegates *accumulate*
across those calls and the last one wins per property, so a fixture that calls
the kernel with the flag on and then again with it off ends up with the flag
off on both surfaces: `IOptions` reports `false` and no normalizer resolves.

## Cache-coherence rule

Normalization runs **before** the L2 write-through and before the
time-series-store append (see [the time-series cache tier
docs](time-series-cache.md)), so turning the flag on over an **already
populated** cache or store directory mixes previously-cached raw records with
newly-cached normalized records under the same keys. A read that spans both
old and new data can silently return a blend of raw and normalized values for
the same record type.

**Enable the flag from the start of a cache directory's life**, or **clear
the L2 snapshot store / time-series store segments** before turning it on for
a host with existing data. There is no automatic re-normalization of already
-cached data — this is the same class of operational rule as the Boutquin.Storage
2.0 wire-format migration note for the time-series store tier.

## Observability

A host that forgets to set the flag gets silently un-normalized data — the
exact failure mode this feature exists to close — so the kernel logs a notice
either way:

The notice describes what the container **actually resolves**, not what a flag
was set to — it resolves each of the nine record types off the built provider
and reports the outcome:

- **Kernel defaults live** → `Information`: registration count and the record
  types registered.
- **Nothing registered, flag off** → `Debug`: a pass-through notice naming the
  flag and stating that records will not be normalized unless the host
  registers its own `INormalizer<T, T>`.
- **The host owns those record types** → `Debug`: how many of the nine the
  host covers. This is the override path working as documented above, so the
  notice does *not* claim records go un-normalized.
- **Nothing resolves at all while the flag reports `true`** → `Warning`: the
  container and the options instance disagree. The only way to reach this is
  a `RemoveAll` opt-out applied to every record type with the flag left on;
  the message says so and names the remedy.

**When the notice fires.** The notice is emitted the **first time
`IDataPipeline` is resolved** from the built container, not when
`AddMarketDataKernel` runs — composition happens before any
`IServiceProvider` exists, so there is nothing to resolve a logger from yet.
`IDataPipeline` is registered as a singleton, so the notice fires **exactly
once per built provider**, however many times the pipeline is subsequently
resolved. A host that never resolves `IDataPipeline` never triggers the
notice — but such a host also never runs the pipeline, so it cannot suffer
the un-normalized-data failure mode the notice guards against.

**Log-level filtering is the host's own call.** A host whose minimum log
level is configured above `Information` (for example, a production host set
to `Warning` or `Error` for noise reasons) will not see either branch of this
notice. That is a deliberate design choice: the off-branch is `Debug`, not
`Warning`, because `false` is the supported default and raising it to
`Warning` would train hosts to ignore warnings on the default path. If a host
wants visibility into this notice under a restrictive global filter, add a
category-level override for the type that emits it
(`Boutquin.MarketData.DependencyInjection.ServiceCollectionExtensions`)
rather than lowering the global minimum level.
