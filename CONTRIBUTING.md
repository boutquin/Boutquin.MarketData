# Contributing to Boutquin.MarketData

Thank you for considering contributing to Boutquin.MarketData! Whether it's reporting a bug, proposing a feature, or submitting a pull request, your input is welcome.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How to Contribute](#how-to-contribute)
  - [Reporting Bugs](#reporting-bugs)
  - [Suggesting Enhancements](#suggesting-enhancements)
  - [Contributing Code](#contributing-code)
- [Style Guides](#style-guides)
  - [Git Commit Messages](#git-commit-messages)
  - [C# Style Guide](#c-style-guide)
  - [Documentation Style Guide](#documentation-style-guide)
  - [Data Correctness](#data-correctness)
- [Pull Request Process](#pull-request-process)
- [License](#license)
- [Community](#community)

## Code of Conduct

This project adheres to the Contributor Covenant [Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Report unacceptable behavior through [GitHub Issues](https://github.com/boutquin/Boutquin.MarketData/issues).

## How to Contribute

### Reporting Bugs

Open an issue on the [Issues](https://github.com/boutquin/Boutquin.MarketData/issues) page with:

- A clear and descriptive title.
- Steps to reproduce the issue (ideally a minimal failing code snippet).
- Expected and actual output, including field values and any relevant `DataProvenance` or `DataIssue` content.
- Reference values (from provider API documentation, ISDA specifications, ISO standards, or a reference implementation) when asserting data correctness.
- Environment: OS, .NET runtime version, package version, data provider, and date range.

### Suggesting Enhancements

Open an issue describing:

- The adapter, record type, pipeline feature, calendar, or day-count convention you would like to see added.
- A primary reference (provider API docs, ISDA specification, ISO standard, exchange rulebook) with the canonical form.
- The intended consumer (pipeline caller, analytics engine, trading framework, etc.).
- Any trade-offs: provider rate limits, authentication model, license terms, data-quality quirks.

### Contributing Code

1. **Fork the repository** and clone your fork locally.
   ```bash
   git clone https://github.com/your-username/Boutquin.MarketData.git
   cd Boutquin.MarketData
   ```

2. **Create a feature branch**:
   ```bash
   git checkout -b feature-or-bugfix-name
   ```

3. **Implement the change** following the style guides below.

4. **Add tests** covering the new behavior. For MarketData code, prefer:
   - xUnit + FluentAssertions unit tests under `tests/Boutquin.MarketData.<Package>.Tests.Unit/` with deterministic inputs and explicit field assertions.
   - Integration tests under `tests/Boutquin.MarketData.Tests.Integration/` when the change affects the full pipeline path (fetch → normalize → snapshot → cache round-trip).
   - Architecture assertions under `tests/Boutquin.MarketData.ArchitectureTests/` when the change introduces new assemblies, new external dependencies, or new cross-layer references.

5. **Record the public API surface** in `src/<Package>/PublicAPI.Unshipped.txt`. The `PublicAPI` analyzer enforces this at build time.

6. **Update `CHANGELOG.md`** under the `[Unreleased]` section.

7. **Run the full gate** before opening a PR:
   ```bash
   dotnet build Boutquin.MarketData.slnx --configuration Release
   dotnet test Boutquin.MarketData.slnx --configuration Release
   dotnet format Boutquin.MarketData.slnx --verify-no-changes
   ```

8. **Push and open a pull request**.

## Style Guides

### Git Commit Messages

- Use the present tense ("Add FRED adapter" not "Added FRED adapter").
- Use the imperative mood ("Move normalizer to helper" not "Moves normalizer to helper").
- Limit the first line to 72 characters.
- Reference issues and pull requests where applicable.

### C# Style Guide

- Follow the conventions documented in `CLAUDE.md` and `.editorconfig` at the repository root.
- Public types are `sealed` unless they are interfaces, abstract base classes, or records (enforced by architecture tests).
- MarketData has **zero** dependencies on Analytics, Trading, or OptionPricing — this is enforced by `Boutquin.MarketData.ArchitectureTests`. Do not add a reference to any domain-layer package.
- Concrete adapters (Tiingo, Frankfurter, FRED, etc.) live in the separate `Boutquin.MarketData.Adapter` repository, not here. The kernel ships contracts and infrastructure only.
- Litmus test for any new type: "Could this be used by both Analytics and Trading without dragging in domain concepts?" If it carries domain-specific semantics, it does not belong in MarketData.

### Documentation Style Guide

All public API additions must satisfy the in-code documentation bar:

- `<summary>` on every public type, constructor, method, property, and enum member.
- `<param>`, `<returns>`, and `<remarks>` per the required-elements checklist in `specs/documentation-style.md`.
- No banned boilerplate phrases ("Provides the ... functionality", "Executes the ... operation", "Gets or sets the ... for this instance", "Input value for ...", "/// Executes ...", "Operation result.", "/// Gets the ...").
- Specification references: name the standard, section, and (where applicable) the ISDA rule or ISO clause number.
- Contracts and invariants must be explicit — rejection conditions, null behaviour, and calendar/day-count edge cases belong in `<remarks>`, not in the summary or left implicit.

Validation commands (must return zero matches before opening a PR):

```bash
# Enforce banned-phrase policy
grep -rn \
  -e 'Provides the .* functionality and related domain behavior' \
  -e 'Executes the .* operation for this component' \
  -e 'The .* input value for the operation' \
  -e 'Gets or sets the .* for this instance' \
  --include='*.cs' src/

# Enforce low-signal phrase policy
grep -rn \
  -e 'Input value for <paramref name=' \
  -e '/// Executes ' \
  -e 'Operation result\.' \
  --include='*.cs' src/

# Enforce accessor-verb property doc policy
grep -rn '/// Gets \w' --include='*.cs' src/
```

### Data Correctness

- State accepted inputs explicitly in docs and tests. Reject out-of-contract inputs at construction time with `ArgumentException` or `ArgumentOutOfRangeException` — do not silently produce wrong results.
- Document calendar and day-count edge cases: end-of-month roll, inverted date ranges, holiday gaps, leap-day behaviour.
- When asserting normalised output, document the transformation rule (e.g., "percent-to-decimal: `7.5` → `0.075`") and the provider-specific quirk that necessitates it.
- Pipeline contracts (`IDataPipeline`, `ISnapshotStore`, `ILocalCache`) must document their failure modes and the `DataIssue` codes they can emit — callers cannot handle what they cannot anticipate.

## Pull Request Process

1. **Ensure the full gate passes**: build (warnings-as-errors), all tests (unit, integration, architecture), and `dotnet format --verify-no-changes`.
2. **Describe your changes** in the PR body: reference the issue, summarise the feature or fix, cite the relevant specification, and note any `PublicAPI.Unshipped.txt` entries added.
3. **Review process**: maintainers will review for correctness, style, and architectural fit. You may be asked to tighten test coverage, add specification citations, or rename symbols.
4. **Merge**: once approved, a maintainer merges the PR. Releases are cut separately via the dual-repo squash workflow on the public repository.

## License

By contributing to Boutquin.MarketData, you agree that your contributions are licensed under the Apache 2.0 License.

## Community

Join the [GitHub Discussions](https://github.com/boutquin/Boutquin.MarketData/discussions) to ask questions, propose adapters, and share usage patterns.

---

Thank you for contributing!
