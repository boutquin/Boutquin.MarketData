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

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit;

/// <summary>
/// Asserts the unknown-symbol invariants that every <see cref="ISecurityResolver"/>
/// implementation must honour (spec §2.3 "Resolver contract for unknown symbols").
/// The test fixture is an identity-on-miss resolver with a single registered
/// dual-listing pair (RY / RY.TO) so we can probe both known and unknown inputs.
/// </summary>
public sealed class SecurityResolverContractTests
{
    private static readonly Symbol s_ry = new("RY");
    private static readonly Symbol s_ryTo = new("RY.TO");
    private static readonly Symbol s_unknown = new("ZZZZ");

    private readonly ISecurityResolver _resolver = new IdentityOnMissResolver();

    [Fact]
    public void ResolvePrimary_returns_input_on_miss()
    {
        _resolver.ResolvePrimary(s_unknown).Should().Be(s_unknown);
    }

    [Fact]
    public void ResolvePrimary_resolves_secondary_to_primary()
    {
        _resolver.ResolvePrimary(s_ryTo).Should().Be(s_ry);
    }

    [Fact]
    public void AreEquivalent_is_reflexive()
    {
        _resolver.AreEquivalent(s_ry, s_ry).Should().BeTrue();
        _resolver.AreEquivalent(s_unknown, s_unknown).Should().BeTrue();
    }

    [Fact]
    public void AreEquivalent_is_commutative()
    {
        _resolver.AreEquivalent(s_ry, s_ryTo).Should().Be(_resolver.AreEquivalent(s_ryTo, s_ry));
        _resolver.AreEquivalent(s_ry, s_unknown).Should().Be(_resolver.AreEquivalent(s_unknown, s_ry));
    }

    [Fact]
    public void AreEquivalent_two_unknown_symbols_are_equivalent_iff_equal()
    {
        var a = new Symbol("UNK1");
        var b = new Symbol("UNK2");

        _resolver.AreEquivalent(a, a).Should().BeTrue();
        _resolver.AreEquivalent(a, b).Should().BeFalse();
    }

    [Fact]
    public void AllListings_returns_non_empty_including_input()
    {
        _resolver.AllListings(s_unknown).Should().Contain(s_unknown).And.NotBeEmpty();
    }

    [Fact]
    public void AllListings_includes_every_registered_alias()
    {
        _resolver.AllListings(s_ry).Should().Contain([s_ry, s_ryTo]);
        _resolver.AllListings(s_ryTo).Should().Contain([s_ry, s_ryTo]);
    }

    /// <summary>Default-style resolver used as the fixture for contract tests.</summary>
    private sealed class IdentityOnMissResolver : ISecurityResolver
    {
        private readonly Dictionary<Symbol, Symbol> _secondaryToPrimary = new()
        {
            [s_ryTo] = s_ry,
        };

        private readonly Dictionary<Symbol, IReadOnlyCollection<Symbol>> _listings = new()
        {
            [s_ry] = new[] { s_ry, s_ryTo },
            [s_ryTo] = new[] { s_ry, s_ryTo },
        };

        public Symbol ResolvePrimary(Symbol symbol) =>
            _secondaryToPrimary.TryGetValue(symbol, out var primary) ? primary : symbol;

        public bool AreEquivalent(Symbol a, Symbol b) =>
            ResolvePrimary(a).Equals(ResolvePrimary(b));

        public IReadOnlyCollection<Symbol> AllListings(Symbol symbol) =>
            _listings.TryGetValue(symbol, out var list) ? list : [symbol];
    }
}
