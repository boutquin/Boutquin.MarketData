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

using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.ReferenceData;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit;

/// <summary>
/// Asserts the §2.4 <see cref="IDividendClassifier"/> contract against a reference
/// fixture implementation that encodes only the documented broad-fallback rules.
/// Implementations that layer issuer-specific metadata on top of these rules
/// must still preserve the invariants asserted here.
/// </summary>
public sealed class IDividendClassifierTests
{
    private readonly IDividendClassifier _classifier = new BroadFallbackClassifier();

    [Theory]
    [InlineData("AAPL", Currency.USD, Currency.USD, DividendType.UsOrdinary)]
    [InlineData("AAPL", Currency.USD, Currency.CAD, DividendType.Foreign)]
    [InlineData("RY.TO", Currency.CAD, Currency.CAD, DividendType.CanadianNonEligible)]
    [InlineData("RY.TO", Currency.CAD, Currency.USD, DividendType.Foreign)]
    [InlineData("VUSA", Currency.GBP, Currency.GBP, DividendType.Foreign)]
    [InlineData("SAN.PA", Currency.EUR, Currency.USD, DividendType.Foreign)]
    public void Broad_fallback_rules_return_expected_category(
        string ticker,
        Currency assetCurrency,
        Currency investorBase,
        DividendType expected)
    {
        _classifier.Classify(new Symbol(ticker), assetCurrency, investorBase)
            .Should().Be(expected);
    }

    [Fact]
    public void Classify_never_returns_default_DividendType()
    {
        // Spec §2.4: default(DividendType) cannot be used as an
        // "unclassifiable" sentinel. Every call must return an explicit member.
        foreach (var asset in Enum.GetValues<Currency>())
        {
            foreach (var investorBase in Enum.GetValues<Currency>())
            {
                var result = _classifier.Classify(new Symbol("ANY"), asset, investorBase);

                // No implicit "default is a sentinel" — all members are legitimate
                // outputs, so instead we assert the returned value is one of the
                // declared members.
                Enum.IsDefined(result).Should().BeTrue(
                    because: "Classify must return an explicit DividendType member, " +
                             "never a synthetic or default-cast value");
            }
        }
    }

    [Fact]
    public void Classify_is_deterministic_for_same_input()
    {
        var symbol = new Symbol("AAPL");

        var first = _classifier.Classify(symbol, Currency.USD, Currency.USD);
        var second = _classifier.Classify(symbol, Currency.USD, Currency.USD);

        first.Should().Be(second);
    }

    [Fact]
    public void Classify_does_not_throw_for_well_formed_inputs()
    {
        // Iterate a representative cross-product of inputs — none should throw.
        var currencies = new[] { Currency.USD, Currency.CAD, Currency.EUR, Currency.JPY };
        var symbols = new[] { new Symbol("AAPL"), new Symbol("RY.TO"), new Symbol("7203.T") };

        foreach (var sym in symbols)
        {
            foreach (var asset in currencies)
            {
                foreach (var investorBase in currencies)
                {
                    var act = () => _classifier.Classify(sym, asset, investorBase);

                    act.Should().NotThrow();
                }
            }
        }
    }

    [Fact]
    public void Classify_treats_mismatched_currencies_as_Foreign()
    {
        _classifier
            .Classify(new Symbol("ANY"), Currency.JPY, Currency.USD)
            .Should().Be(DividendType.Foreign);
    }

    /// <summary>
    /// Reference classifier that encodes the §2.4 broad-fallback rules only.
    /// Trading's production classifier layers issuer-specific detection (REIT,
    /// eligible-dividend, qualified-dividend predicates) on top; those mappings
    /// must still return an explicit <see cref="DividendType"/> member.
    /// </summary>
    private sealed class BroadFallbackClassifier : IDividendClassifier
    {
        public DividendType Classify(Symbol symbol, Currency assetCurrency, Currency investorBaseCurrency)
        {
            if (assetCurrency != investorBaseCurrency)
            {
                return DividendType.Foreign;
            }

            return investorBaseCurrency switch
            {
                Currency.USD => DividendType.UsOrdinary,
                Currency.CAD => DividendType.CanadianNonEligible,
                _ => DividendType.Foreign,
            };
        }
    }
}
