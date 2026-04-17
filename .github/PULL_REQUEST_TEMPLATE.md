## Summary

Brief description of what this PR does.

## Changes

- ...

## Related Issues

Closes #

## Checklist

- [ ] Code compiles with zero warnings (`TreatWarningsAsErrors` enabled)
- [ ] All existing tests pass (unit, integration, architecture)
- [ ] New tests added for new functionality — unit tests under `tests/Boutquin.MarketData.<Package>.Tests.Unit/`; integration tests under `tests/Boutquin.MarketData.Tests.Integration/` when pipeline behavior changes; architecture tests under `tests/Boutquin.MarketData.ArchitectureTests/` when new assemblies or external dependencies are introduced
- [ ] `PublicAPI.Unshipped.txt` updated for any new or changed public API
- [ ] `dotnet format --verify-no-changes` produces no changes
- [ ] XML doc comments are complete (no banned phrases; semantic description; `<remarks>` required on core contracts and key implementations — see [CONTRIBUTING.md](../CONTRIBUTING.md#documentation-style-guide))
- [ ] `CHANGELOG.md` updated under `[Unreleased]` (if user-visible change)
- [ ] No new cross-layer dependencies introduced: MarketData must not reference Analytics, Trading, or OptionPricing; concrete adapters stay in `Boutquin.MarketData.Adapter` (architecture tests enforce this)
