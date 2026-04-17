// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions;

/// <summary>
/// Resolves dual-listed securities to their primary listing and determines
/// equivalence across venues (for example, RY on NYSE ↔ RY.TO on TSX).
/// </summary>
/// <remarks>
/// <para>
/// A dual-listing resolver is a <em>data-layer</em> concern — the mapping from
/// secondary venue tickers to their primary listing is a market fact, independent
/// of any trading strategy. Pipelines that aggregate <see cref="Records.Bar"/> or
/// <see cref="Records.FxRate"/> data across providers should funnel identifiers
/// through a resolver before keying downstream collections so that cross-venue
/// history collapses onto a single canonical series.
/// </para>
/// <para>
/// <strong>Unknown-symbol contract.</strong> Implementations MUST be defined on
/// every well-formed <see cref="Symbol"/> — even those not present in the
/// resolver's mapping table. Specifically:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <see cref="ResolvePrimary"/> returns its argument unchanged when the symbol
///     has no registered mapping. It never throws for a well-formed
///     <see cref="Symbol"/> and never returns <c>default(Symbol)</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="AreEquivalent"/> is reflexive (<c>AreEquivalent(a, a)</c> is
///     always <see langword="true"/>) and commutative. Two unmapped symbols are
///     equivalent iff they are equal by structural <see cref="Symbol"/>
///     equality.
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="AllListings"/> always returns a non-null, non-empty collection
///     containing at least the argument itself.
///     </description>
///   </item>
/// </list>
/// </remarks>
public interface ISecurityResolver
{
    /// <summary>
    /// Returns the primary listing for the given symbol. If <paramref name="symbol"/>
    /// is not a known secondary listing, returns <paramref name="symbol"/> unchanged.
    /// </summary>
    /// <param name="symbol">The symbol to resolve.</param>
    /// <returns>The primary-listing symbol, or <paramref name="symbol"/> itself when no mapping is registered.</returns>
    Symbol ResolvePrimary(Symbol symbol);

    /// <summary>
    /// Determines whether two symbols are equivalent — that is, whether they resolve
    /// to the same primary listing.
    /// </summary>
    /// <param name="a">The first symbol.</param>
    /// <param name="b">The second symbol.</param>
    /// <returns>
    /// <see langword="true"/> if both symbols resolve to the same primary listing
    /// under <see cref="ResolvePrimary"/>; otherwise <see langword="false"/>.
    /// </returns>
    bool AreEquivalent(Symbol a, Symbol b);

    /// <summary>
    /// Returns all known listings (primary and every registered secondary) for the
    /// security identified by <paramref name="symbol"/>.
    /// </summary>
    /// <param name="symbol">Any known listing of the security.</param>
    /// <returns>
    /// A non-null, non-empty collection that always contains <paramref name="symbol"/>
    /// itself, plus every other listing the resolver knows about.
    /// </returns>
    IReadOnlyCollection<Symbol> AllListings(Symbol symbol);
}
