# Day-count conventions

Day-count conventions decide how time is measured for accrual, discounting, and
rate conversion. Different market segments use different rules, so consumers
reach every convention through `DayCountResolver.Resolve(code)` rather than a
direct reference — swapping conventions is then a single-line configuration
change.

## Shipped conventions

| Code | Class | Market |
|------|-------|--------|
| `ACT/360` | `Actual360` | US money market, LIBOR (most), SOFR |
| `ACT/365F` | `Actual365Fixed` | GBP/CAD rates, many government bonds |
| `30/360` (Bond Basis) | `Thirty360` | US corporate and municipal bonds |
| `ACT/ACT` (ICMA in-period) | `ActualActualInPeriod` | US Treasury / euro government bonds |
| `30E/360` (Eurobond) | `ThirtyE360` | Euro-denominated corporate bonds |

Deliberately not shipped: `ACT/ACT ISDA`, `BUS/252`, `NL/365`. Add them when a
concrete consumer surfaces the need, with their own corner-case tests.

## Alias table

`DayCountResolver.Resolve` accepts these case-insensitive aliases:

| Canonical code | Aliases |
|----------------|---------|
| `ACT/360` | `ACTUAL/360` |
| `ACT/365F` | `ACT/365`, `ACTUAL/365F` |
| `30/360` | `THIRTY/360` |
| `ACT/ACT` | `ACTUAL/ACTUAL`, `ACT/ACT ICMA`, `ACT/ACT BOND` |
| `30E/360` | `EUROBOND`, `EUROBOND BASIS` |

Unknown codes throw `NotSupportedException` with the rejected code in the
message — silently substituting a convention would produce wrong accrual
values with no visible failure mode.

## `ACT/ACT` disambiguation

`ACT/ACT` without further qualification resolves to ICMA (in-period), because
that is the convention most new consumers reach for (US Treasury / euro
government bonds). The ISDA variant is not yet shipped; when it lands, the
`ACT/ACT ISDA` alias will be added without changing this default.

## `ActualActualInPeriod` usage

ICMA Rule 251 requires coupon context (the coupon period the interval falls
inside, plus the coupon frequency). The five-argument overload supplies it:

```csharp
var yf = ActualActualInPeriod.Instance.YearFraction(
    start: new DateOnly(2026, 1, 15),
    end: new DateOnly(2026, 7, 15),
    periodStart: new DateOnly(2026, 1, 15),
    periodEnd: new DateOnly(2026, 7, 15),
    couponFrequency: 2);           // semiannual
// => 0.5 exactly
```

The two-argument overload (no coupon context) falls back to ACT/365.25 — the
ICMA fallback for short horizons. Prefer the five-argument version whenever
accruing across coupon dates matters.

## `ThirtyE360` vs `Thirty360` (Bond Basis)

Both cap day counts at 30 per ISDA §4.16. The Eurobond variant applies
**no** end-of-month adjustment to `D2`:

| Interval | `Thirty360` (Bond Basis) | `ThirtyE360` (Eurobond) |
|----------|--------------------------|-------------------------|
| Jan 15 → Jul 15 | `(30·6) / 360 = 0.5` | `(30·6) / 360 = 0.5` |
| Jan 31 → Feb 28 | `(30·1) / 360 = 30/360` | `(30·1 + (28-30)) / 360 = 28/360` |

The Eurobond rule is simpler and is the standard for euro-denominated
corporate bond accruals.

## Resolver contract

```csharp
IYearFractionCalculator calc = DayCountResolver.Resolve("ACT/ACT");
```

Every `IYearFractionCalculator` is a stateless singleton — callers can
cache the reference safely and call `YearFraction` concurrently. The
`Code` property returns the canonical ISDA/ICMA code (not the alias the
caller supplied).
