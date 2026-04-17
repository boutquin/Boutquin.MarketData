---
name: Feature Request
about: Suggest an idea for this project
title: ''
labels: enhancement
assignees: ''
---

## Problem

A clear description of the problem or limitation you're experiencing.

## Proposed Solution

Describe the feature or change you'd like to see. For a new data source adapter, please include:

- The provider name and public API documentation URL.
- The record type(s) it would produce (`Bar`, `FxRate`, `ScalarObservation`, `YieldCurveQuote`, or a new canonical record).
- Rate limits, authentication model (API key, OAuth, anonymous), and license terms (free / delayed / commercial).
- Any known data-quality quirks (split-adjustment, missing fields, weekend/holiday gaps).

For a new pipeline feature, normalization rule, calendar, or day-count convention, include:

- The relevant specification or standard (ISDA, ISO, exchange rulebook, central bank documentation).
- A worked example of the expected input → output transformation.
- Any edge cases or instrument-specific carve-outs the implementation must handle.

## Alternatives Considered

Any alternative solutions or existing pipeline components you've considered and why they don't fit.

## Additional Context

Add any other context, sample payloads, reference implementations, or benchmark data.
