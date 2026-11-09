# Building and release verification

Use the SDK selected by `global.json` (10.0.403 or a later 10.0 feature band). An older 10.0.1xx compiler cannot load Storage's source generator. Published consumption restores from NuGet.org. Before Storage publication, local qualification uses its exact sealed archives through the explicit prepared-feed override below.

`dependency-source.json` records signed Domain archive hashes and the exact prepared Storage archive, payload and content hashes with their source revisions. The prepared verifier accepts original local bytes, or a valid NuGet repository signature with an identical original payload inventory after publication. The content hash excludes the signature; the archive hash includes it. The checker rejects substituted same-version archives and unexpected package sources.

```bash
python3 scripts/check-runner-dependencies.py
export NUGET_PACKAGES="$(mktemp -d)"
export NUGET_HTTP_CACHE_PATH="$(mktemp -d)"
dotnet restore Boutquin.MarketData.slnx --configfile nuget.ci.config --locked-mode --no-http-cache
python3 scripts/check-runner-dependencies.py --packages "$NUGET_PACKAGES"
dotnet build Boutquin.MarketData.slnx -c Release --no-restore
dotnet test Boutquin.MarketData.slnx -c Release --no-build --no-restore
dotnet format Boutquin.MarketData.slnx --verify-no-changes --no-restore
```

CI repeats cold restore, provenance, build, tests, formatting and documentation checks on Linux, macOS and Windows. Package inspection checks all twelve packages, XML docs, dependency versions, matching portable PDBs, public SourceLink, and actual assembly version `3.0.0.0`, file version matching the package core (`3.0.0.0` for 3.0.0), and informational version containing the exact source commit.

Release archives are built once from the exact public source commit, qualified, and frozen in a SHA-256 manifest. The publication workflow accepts an exact stable `vMAJOR.MINOR.PATCH` or numeric `vMAJOR.MINOR.PATCH-rc.N[.N…]` tag and manifest hash, downloads that release's original assets, verifies source and package identities, and uploads those same bytes to NuGet.org. It never repacks a qualified identity. NuGet's repository signature is subsequently checked along with equality of every original package payload entry. Existing RC archives remain immutable.

The publisher authenticates to NuGet.org with trusted publishing: the job's GitHub OIDC token, issued for the `release` environment, is exchanged for a one-hour API key, so no long-lived key is stored. Each release follows the same order. Date the `CHANGELOG.md` section and align the README version and tag links before the source push; `python3 scripts/check-release-docs.py <version>` enforces this, and the publication workflow runs it against the tagged source for stable tags. Run the verification workflow on the public source, create the tag and release assets, then dispatch the publisher. Publish MarketData before Adapter, verify signed public payload equality and cold public consumption from an empty package folder (a populated global NuGet cache can silently serve a different build of the same version), then release the Adapter. Never rebuild sealed packages.

For prepublication qualification, set `BOUTQUIN_PREPARED_FEED` to the sealed Storage archive directory. Generate a separate configuration with `python3 scripts/prepared-dependencies.py --config artifacts/prepared.nuget.config`, then restore using that configuration with `--locked-mode`. Exact package IDs map to the prepared feed; other packages, including Domain, map to NuGet.org. Do not copy old same-version archives into this feed. The public configuration remains NuGet.org-only. Private CI uses read-only access to the pinned archive commit and does not publish packages.

At a stable release boundary, `python3 scripts/check-api-release-baseline.py --release` must pass: every public package has a fingerprinted shipped API snapshot and no pending declarations. Ordinary development checks permit pending declarations until release promotion.
