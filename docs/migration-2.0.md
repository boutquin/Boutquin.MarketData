# Migrating to 2.0 RC

Use .NET 10 and the same explicit `2.0.0-rc.20261027.2` version for every MarketData package. The storage implementation uses published Boutquin.Storage 2.0.0; restore from NuGet.org. Numerics is not a kernel dependency.

Review the [changelog](../CHANGELOG.md) for API changes and [dividend contracts](dividends.md) for classification and coverage behavior. Currency and shared value objects come from Boutquin.Domain. Concrete providers are in the separate MarketData.Adapter packages.

The optional time-series cache remains disabled by default. Pre-2.0 segments are not wire-compatible: remove affected stored segments and re-ingest; preserve source data before migration. Noncanonical factor/tenor keys also require re-ingestion. See the [time-series migration and recovery guide](time-series-cache.md), including checkpoint directories, snapshots, quarantine, and custom persisted Bloom factories.

This is a release candidate, not the final 2.0.0 release. Qualification covers deterministic fixtures, robustness regressions, independent calendar/day-count oracles, package consumers, and supported CI platforms. It does not certify upstream provider availability or live-data entitlements.
