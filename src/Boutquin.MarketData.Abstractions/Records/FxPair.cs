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

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// Strongly-typed identifier for a foreign-exchange currency pair.
/// </summary>
/// <param name="BaseCurrency">The ISO 4217 currency of the base leg (e.g., <see cref="Currency.EUR"/>).</param>
/// <param name="QuoteCurrency">The ISO 4217 currency of the quote leg (e.g., <see cref="Currency.USD"/>).</param>
/// <remarks>
/// <para>
/// <see cref="ToString"/> returns the market-standard format "BASE/QUOTE" (e.g., "EUR/USD").
/// Use this as a dictionary key or in request specifications to avoid stringly-typed pair references.
/// </para>
/// <para>
/// Use <see cref="MarketConvention"/> to obtain the pair in standard interbank quoting order.
/// Use <see cref="Invert"/> to reverse the quoting direction.
/// </para>
/// </remarks>
public sealed record FxPair(Currency BaseCurrency, Currency QuoteCurrency)
{
    /// <summary>
    /// G10 currency priority for determining standard base/quote ordering.
    /// Higher rank = more likely to be the base currency.
    /// </summary>
    /// <remarks>
    /// Follows the interbank market convention: EUR is always base, then GBP, AUD, NZD, USD.
    /// USD is base against most other currencies but quote against the four above.
    /// Currencies not in this list get rank 0 (always quoted against G10).
    /// </remarks>
    /// <remarks>
    /// Currencies not in this dictionary get rank 0 (always quoted against G10). NZD is
    /// not yet in the <see cref="s_currencyRank"/> table; rank 5 is reserved for when it
    /// is added in a future release.
    /// </remarks>
    private static readonly Dictionary<Currency, int> s_currencyRank = new()
    {
        [Currency.EUR] = 8,
        [Currency.GBP] = 7,
        [Currency.AUD] = 6,
        [Currency.USD] = 4,
        [Currency.CAD] = 3,
        [Currency.CHF] = 2,
        [Currency.JPY] = 1,
    };

    /// <inheritdoc />
    public override string ToString() => $"{BaseCurrency}/{QuoteCurrency}";

    /// <summary>
    /// Returns this pair with base and quote currencies swapped.
    /// </summary>
    public FxPair Invert() => new(QuoteCurrency, BaseCurrency);

    /// <summary>
    /// Returns the pair in standard interbank market convention ordering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The interbank convention determines which currency is quoted as the base:
    /// EUR is always base; GBP is base except vs. EUR; AUD is base except
    /// vs. EUR and GBP; USD is base against all except EUR, GBP, AUD.
    /// </para>
    /// <para>
    /// For example, <c>new FxPair(Currency.USD, Currency.EUR).MarketConvention()</c>
    /// returns <c>FxPair(Currency.EUR, Currency.USD)</c> because EUR is always the base.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The same pair if already in market convention order, or an inverted pair if not.
    /// </returns>
    public FxPair MarketConvention()
    {
        var baseRank = s_currencyRank.GetValueOrDefault(BaseCurrency, 0);
        var quoteRank = s_currencyRank.GetValueOrDefault(QuoteCurrency, 0);

        if (baseRank > quoteRank)
        {
            return this;
        }

        if (quoteRank > baseRank)
        {
            return Invert();
        }

        // Equal rank — use ordinal name comparison as tiebreaker (stable across cultures).
        return string.CompareOrdinal(BaseCurrency.ToString(), QuoteCurrency.ToString()) <= 0
            ? this
            : Invert();
    }

    /// <summary>
    /// The standard number of settlement days for this currency pair.
    /// </summary>
    /// <remarks>
    /// Most G10 pairs settle T+2. USD/CAD settles T+1. Same-currency pairs settle T+0.
    /// Pairs involving non-G10 currencies default to T+2 but may differ in practice.
    /// </remarks>
    public int StandardSettlementDays
    {
        get
        {
            if (BaseCurrency == QuoteCurrency)
            {
                return 0;
            }

            // USD/CAD (or CAD/USD) settles T+1.
            var pair = MarketConvention();
            if (pair.BaseCurrency == Currency.USD && pair.QuoteCurrency == Currency.CAD)
            {
                return 1;
            }

            return 2;
        }
    }
}
