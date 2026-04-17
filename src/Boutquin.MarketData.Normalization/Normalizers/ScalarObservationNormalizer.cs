// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License").
//  You may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//  See the License for the specific language governing permissions and
//  limitations under the License.
//

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Records;

namespace Boutquin.MarketData.Normalization.Normalizers;

/// <summary>
/// Produces a canonical <see cref="ScalarObservation"/> by converting percent-form values to
/// decimal form and lowercasing unit strings so downstream consumers do not have to branch on
/// whether <c>0.045</c> and <c>4.5%</c> represent the same rate.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline role: normalizer for economic-series adapters (FRED, ECB, Bank of Canada, etc.)
/// whose native unit strings are inconsistent (<c>Percent</c>, <c>pct</c>, <c>%</c>) and whose
/// rate values are frequently published in percent rather than decimal. Running this
/// normalizer produces a consistent <c>(value, units="decimal")</c> shape that curve builders
/// and analytics can consume without per-source special cases.
/// </para>
/// <para>
/// Percent detection: case-insensitive match against <c>percent</c>, <c>pct</c>, and
/// <c>%</c>. When matched, the value is divided by 100 and the units are rewritten to
/// <c>decimal</c> so the transformation is recorded in the output itself — not just as a
/// side-effect. Other unit strings are lowercased for stability but values pass through
/// unchanged.
/// </para>
/// <para>
/// Idempotency: the normalizer is safe to apply once. It is NOT idempotent under repeated
/// application because applying it twice to a matched-unit input would divide by 100 a
/// second time — however the second pass sees <c>units="decimal"</c> which falls through
/// the match set, so in practice repeated invocation produces the same result as a single
/// invocation as long as the first one rewrote the units. Callers should not rewrap input
/// units as <c>percent</c> after normalization, and pipelines should run this normalizer
/// exactly once per record.
/// </para>
/// <para>
/// Common mistake: feeding already-decimal values with units like <c>Percent</c> (because
/// the source mislabels them). The normalizer will divide by 100 and produce tiny, wrong
/// values. Validate source unit conventions before first use of a new adapter.
/// </para>
/// </remarks>
public sealed class ScalarObservationNormalizer : INormalizer<ScalarObservation, ScalarObservation>
{
    /// <summary>
    /// Canonical units label emitted after a percent-to-decimal conversion. Kept as a named
    /// constant rather than a magic string so future unit-taxonomy work (bps → decimal, bp →
    /// decimal) can reference the same symbol and downstream consumers can compare against it.
    /// </summary>
    public const string CanonicalDecimalUnits = "decimal";

    // Ordinal-ignore-case so `%` (a single code point) and punctuation-based variants compare
    // without locale influence — a Turkish-locale ToLower would not fold these correctly.
    private static readonly HashSet<string> s_percentUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "percent",
        "pct",
        "%",
    };

    /// <inheritdoc />
    public ScalarObservation Normalize(ScalarObservation raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var units = raw.Units.Trim();

        if (s_percentUnits.Contains(units))
        {
            // Rewrite units to canonical "decimal" so a second pass no longer matches the
            // percent set — this is what makes a one-shot normalization safe to re-run on
            // stored snapshots.
            return new ScalarObservation(raw.Date, raw.Value / 100m, CanonicalDecimalUnits);
        }

        return new ScalarObservation(raw.Date, raw.Value, units.ToLowerInvariant());
    }
}
