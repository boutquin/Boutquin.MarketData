#!/usr/bin/env python3
"""Regenerate bounded independent fixtures with QuantLib 1.44 (verification only)."""
import datetime as dt
import json
from pathlib import Path
import random
import QuantLib as ql

assert ql.__version__ == "1.44"
root = Path(__file__).resolve().parents[1]
start = dt.date(2020, 1, 1)
end = dt.date(2026, 10, 26)
dates = [start + dt.timedelta(days=index) for index in range((end - start).days + 1)]

def qdate(date: dt.date) -> ql.Date:
    return ql.Date(date.day, date.month, date.year)

calendars = {
    "USNY": ql.UnitedStates(ql.UnitedStates.NYSE),
    "CATO": ql.Canada(ql.Canada.TSX),
    "GBLO": ql.UnitedKingdom(ql.UnitedKingdom.Settlement),
    "TARGET": ql.TARGET(),
    "USSF": ql.UnitedStates(ql.UnitedStates.GovernmentBond),
}
calendar_cases = {code: "".join("1" if cal.isBusinessDay(qdate(date)) else "0" for date in dates)
                  for code, cal in calendars.items()}
rng = random.Random(20261026)
pairs = [(dt.date(year, month, day), dt.date(year, month, day) + dt.timedelta(days=span))
         for year in (2020, 2021, 2024, 2025) for month, day in ((1, 31), (2, 28), (3, 31), (12, 31))
         for span in (0, 1, 28, 30, 31, 59, 90, 180, 365, 366)]
for _ in range(1000):
    first = dt.date(2010, 1, 1) + dt.timedelta(days=rng.randrange(7305))
    pairs.append((first, first + dt.timedelta(days=rng.randrange(1097))))
counters = {"ACT/360": ql.Actual360(), "ACT/365F": ql.Actual365Fixed(),
            "30/360": ql.Thirty360(ql.Thirty360.BondBasis),
            "30E/360": ql.Thirty360(ql.Thirty360.European)}
day_counts = [{"code": code, "start": first.isoformat(), "end": last.isoformat(),
               "expected": counter.yearFraction(qdate(first), qdate(last))}
              for code, counter in counters.items() for first, last in pairs]
icma = []
for year in (2020, 2021, 2024, 2025):
    for frequency in (1, 2, 4):
        a = dt.date(year, 1, 15)
        b = dt.date(year + 1, 1, 15) if frequency == 1 else dt.date(year, 1 + 12 // frequency, 15)
        for fraction in (0, .25, .5, .75, 1):
            last = a + dt.timedelta(days=int((b - a).days * fraction))
            icma.append({"start": a.isoformat(), "end": last.isoformat(), "periodStart": a.isoformat(),
                         "periodEnd": b.isoformat(), "frequency": frequency,
                         "expected": ql.ActualActual(ql.ActualActual.ISMA).yearFraction(qdate(a), qdate(last), qdate(a), qdate(b))})
payload = {"oracle": "QuantLib", "version": ql.__version__, "source": "https://github.com/lballabio/QuantLib/tree/v1.44",
           "seed": 20261026, "calendarStart": start.isoformat(), "calendarEnd": end.isoformat(),
           "calendarChecks": len(dates) * len(calendars), "calendars": calendar_cases,
           "dayCountChecks": len(day_counts) + len(icma), "dayCounts": day_counts, "icma": icma}
path = root / "tests/Boutquin.MarketData.Tests.Integration/Fixtures/financial-oracle.json"
path.parent.mkdir(exist_ok=True)
path.write_text(json.dumps(payload, separators=(",", ":")) + "\n", encoding="utf-8")
print(json.dumps({key: payload[key] for key in ("oracle", "version", "calendarChecks", "dayCountChecks")}))
