# TSX historical calendar and compatibility packs

`CaTsxCalendar` implements TSX equity trading days. Victoria Day is the Monday strictly before
May 25. Saturday Christmas observes Monday December 27 and Tuesday December 28, leaving
December 24 open (an early close still counts as a trading day). Sunday Christmas observes
December 26 and 27; Friday Christmas observes Boxing Day on the following Monday. New Year
and Canada Day on a Saturday are observed on Monday, rather than on the preceding Friday.

Family Day starts in 2008. The exceptional full-day closure on September 12, 2001 is included;
September 11 remains a business day because trading took place before its emergency early close.
A daily calendar does not model session hours, and future exceptional closures need explicit updates.

References:
- [TSX calendar](https://www.tsx.com/en/trading/calendars-and-trading-hours/calendar)
- [Victoria Day 2026 exchange notice](https://tsx.com/en/trading/toronto-stock-exchange/trading-notices?id=1114)
- [Christmas 2021 exchange notice](https://www.tsx.com/en/news?id=870&month=11&year=2021)
- [Ontario Family Day introduction](https://www.ontario.ca/document/employment-standard-act-policy-and-interpretation-manual/part-x-public-holidays)
- [Bank of Canada, September 12, 2001](https://www.bankofcanada.ca/2001/09/bank-canada-update-financial-markets/)

## Calendar-only adoption

The normal build retains its project reference to current Abstractions. For an application still
using an older Abstractions package, pass `CalendarAbstractionsPackageVersion` to build and pack
Calendars against that exact package ABI. This does not repack or upgrade the kernel itself:

```bash
dotnet pack src/Boutquin.MarketData.Calendars/Boutquin.MarketData.Calendars.csproj \
  --configuration Release --configfile /path/to/consumer/nuget.config \
  /p:CalendarAbstractionsPackageVersion=1.1.0-local-20260726 \
  /p:MinVerVersionOverride=<unique-calendar-version> \
  /p:AssemblyVersion=1.0.0.0 /p:FileVersion=1.1.1.0 --output nupkg
```

Verify the packed dependency version, assembly binding identity and the consuming application's
resolved assets. A package built against an older ABI must pass the consumer's calendar and full
application checks before adoption. It does not certify the consumer's other pinned packages.
