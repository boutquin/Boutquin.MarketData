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

using Boutquin.Storage.Domain.Attributes;

namespace Boutquin.MarketData.Abstractions.Keys;

/// <summary>
/// Composite partition key for a single yield-curve tenor's series:
/// <c>(Provider, CurveId, Tenor)</c>. Example: <c>("boc", "CAD-ZERO", "10Y")</c>.
/// </summary>
/// <param name="Provider">Adapter-assigned provider identifier (e.g. <c>"boc"</c>, <c>"ustreasury"</c>). Stays <c>string</c> (open registry; wrappers illegal in keys, BSSG014).</param>
/// <param name="CurveId">Curve identifier (<see cref="Requests.YieldCurveQuoteRequest.DatasetKey"/> — the <c>YieldCurveId.Value</c>, optionally suffixed <c>/Exchange</c>), stringified at the builder boundary.</param>
/// <param name="Tenor">The per-node maturity discriminant (e.g. <c>"3M"</c>, <c>"1Y"</c>, <c>"10Y"</c>).</param>
/// <remarks>
/// <para><b>Tenor in the key:</b> <see cref="Records.YieldCurveQuote"/>
/// stores one row per <c>(Date, Tenor, Rate)</c>.
/// A curve snapshot is the set of nodes sharing a <c>Date</c>, so the store's composite LSM key
/// <c>(DatasetKey, Date)</c> would collide across tenors of one curve-day unless <c>Tenor</c> is
/// part of the dataset key. Hoisting it here makes each tenor its own daily series: one tenor's
/// history is a <c>(Provider, CurveId, Tenor)</c> prefix scan, and a full curve-as-of-date read is
/// a <c>(Provider, CurveId, *)</c> range filtered to one <c>Date</c>.</para>
/// <para><b>Exchange handling:</b> the regional hint is folded into <c>CurveId</c> at the request
/// layer (<c>CurveId[/Exchange]</c>), so the builder passes that folded string as the
/// <see cref="CurveId"/> column rather than adding a fourth nullable <c>Exchange</c> field —
/// mirroring the request's existing partitioning and keeping the key flat (Q17).</para>
/// <para><b>Discriminant identity.</b> <c>Tenor</c> is simultaneously a record body
/// column and this key's discriminant, and normalization rewrites that column. If the key producer
/// and the body producer canonicalised the label differently — or one canonicalised and the other
/// did not — one logical tenor would fork into two persisted series that the discriminant-blind
/// membership filter then merges. <see cref="CanonicalTenor"/> is the single owner of that identity
/// relation: the fan-out applies it to every projected key regardless of whether any normalizer is
/// registered, and <c>YieldCurveQuoteNormalizer</c> applies the same function to the body, so the
/// projected key is invariant under normalization.</para>
/// <para>See <see cref="PriceBarDatasetKey"/> remarks for the Storage 2.0 tagged-ordered-wire
/// design and the lexicographic ordering guarantee.</para>
/// </remarks>
[Key]
public readonly partial record struct YieldCurveQuoteDatasetKey(
    [property: Tag(1)] string Provider,
    [property: Tag(2)] string CurveId,
    [property: Tag(3)] string Tenor)
{
    /// <summary>
    /// The canonical spelling of a tenor label — the identity relation for the <see cref="Tenor"/>
    /// discriminant column.
    /// </summary>
    /// <param name="tenor">The raw tenor label as published by the source (e.g. <c>" 10y "</c>).</param>
    /// <returns>The trimmed, invariant-uppercased label (e.g. <c>"10Y"</c>).</returns>
    /// <remarks>
    /// Idempotent — <c>CanonicalTenor(CanonicalTenor(x)) == CanonicalTenor(x)</c> — which is what
    /// makes a key projected through it invariant under normalization.
    /// </remarks>
    /// <exception cref="NullReferenceException">
    /// <paramref name="tenor"/> is <see langword="null"/>. Canonical records carry non-nullable
    /// string columns; a null column is a producer defect, surfaced rather than persisted.
    /// </exception>
    public static string CanonicalTenor(string tenor) => tenor.Trim().ToUpperInvariant();
}
