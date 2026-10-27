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
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Keys;

// M-CR (M-3') ordered-wire byte-lex contract for [Key] dataset keys.
//
// The [Key] generator emits an ordered wire whose invariant is:
//   byte-lex(Serialize(a), Serialize(b)) sign-agrees with a.CompareTo(b)
// and CompareTo applies field precedence in DECLARATION order (the composite-key contract a range
// scan relies on: a prefix triple hits a contiguous LSM key range). These tests pin both halves —
// CompareTo follows declaration order, and the serialized bytes order the same way — so a future
// reorder of key properties (which silently shifts the persisted key bytes) is caught.
public sealed class DatasetKeyOrderingTests
{
    private static byte[] Wire(PriceBarDatasetKey k)
    {
        using var ms = new MemoryStream();
        k.Serialize(ms);
        return ms.ToArray();
    }

    private static byte[] Wire(FxRateDatasetKey k)
    {
        using var ms = new MemoryStream();
        k.Serialize(ms);
        return ms.ToArray();
    }

    private static int ByteLex(byte[] a, byte[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var i = 0; i < n; i++)
        {
            var c = a[i].CompareTo(b[i]);
            if (c != 0)
            {
                return Math.Sign(c);
            }
        }

        return Math.Sign(a.Length.CompareTo(b.Length));
    }

    [Fact]
    public void PriceBar_CompareTo_FollowsDeclarationOrder()
    {
        // Declaration order: Provider, Frequency, Ticker, Exchange. A higher-precedence field
        // dominates every lower one regardless of the lower fields' values.
        var basis = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "MMMM", ExchangeCode.XNAS);

        // Provider dominates Frequency/Ticker/Exchange.
        var laterProvider = new PriceBarDatasetKey("twelvedata", DataFrequency.Tick, "AAAA", ExchangeCode.XNYS);
        basis.CompareTo(laterProvider).Should().BeLessThan(0);

        // Same Provider: Frequency dominates Ticker/Exchange. Tick(0) < Daily(2).
        var sameProviderEarlierFreq = new PriceBarDatasetKey("tiingo", DataFrequency.Tick, "ZZZZ", ExchangeCode.XNYS);
        basis.CompareTo(sameProviderEarlierFreq).Should().BeGreaterThan(0);

        // Same Provider+Frequency: Ticker dominates Exchange.
        var sameToTickerEarlier = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AAAA", ExchangeCode.XNYS);
        basis.CompareTo(sameToTickerEarlier).Should().BeGreaterThan(0);

        // Same Provider+Frequency+Ticker: Exchange (lowest precedence) breaks the final tie.
        var sameToExchange = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "MMMM", ExchangeCode.XNYS);
        basis.CompareTo(sameToExchange).Should().NotBe(0);
    }

    [Fact]
    public void PriceBar_ByteLex_SignAgreesWithCompareTo()
    {
        var keys = new[]
        {
            new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", ExchangeCode.XNYS),
            new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "BND", ExchangeCode.XNYS),
            new PriceBarDatasetKey("tiingo", DataFrequency.Tick, "AGG", ExchangeCode.XNYS),
            new PriceBarDatasetKey("twelvedata", DataFrequency.Daily, "AGG", ExchangeCode.XNYS),
            new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", ExchangeCode.XNAS),
        };

        foreach (var a in keys)
        {
            foreach (var b in keys)
            {
                Math.Sign(a.CompareTo(b)).Should().Be(
                    ByteLex(Wire(a), Wire(b)),
                    "ordered-wire byte-lex must sign-agree with CompareTo for {0} vs {1}",
                    a,
                    b);
            }
        }
    }

    [Fact]
    public void PriceBar_SamePrefix_RangeIsContiguous()
    {
        // Two tickers sharing (Provider, Frequency) bracket a third whose ticker sorts between
        // them; nothing with a different (Provider, Frequency) prefix can fall inside the bracket.
        var low = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AAA", ExchangeCode.XNYS);
        var mid = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "MMM", ExchangeCode.XNYS);
        var high = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "ZZZ", ExchangeCode.XNYS);
        var otherPrefix = new PriceBarDatasetKey("tiingo", DataFrequency.Weekly, "MMM", ExchangeCode.XNYS);

        low.CompareTo(mid).Should().BeLessThan(0);
        mid.CompareTo(high).Should().BeLessThan(0);

        // A different Frequency prefix (Weekly = 3 > Daily = 2) sorts entirely after the Daily band.
        otherPrefix.CompareTo(high).Should().BeGreaterThan(0);
    }

    // M-2.6: FxRateDatasetKey reshaped from all-string to (Provider:string, BaseCurrency:enum,
    // QuoteCurrency:enum, Frequency:enum). The two Currency columns and the DataFrequency
    // column are now encoded as enum-ordered wire, so re-pin the byte-lex ⇔ CompareTo invariant
    // for the new enum-column key shape (the all-string PriceBar test above does not cover it).

    // Currency uses ISO 4217 numeric codes as enum values (AUD=36, CAD=124, JPY=392, GBP=826,
    // USD=840, EUR=978), so enum ordering is by that numeric code, NOT alphabetical. DataFrequency
    // is ordinal (Tick=0, Intraday=1, Daily=2, Weekly=3, Monthly=4). The byte-lex ⇔ CompareTo
    // invariant holds against whatever ordering the underlying values impose; these tests assert it.

    [Fact]
    public void FxRate_CompareTo_FollowsDeclarationOrder()
    {
        // Declaration order: Provider, BaseCurrency, QuoteCurrency, Frequency. A higher-precedence
        // field dominates every lower one regardless of the lower fields' values.
        var basis = new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.USD, DataFrequency.Daily);

        // Provider dominates the three enum columns ("frankfurter" < "twelvedata" ordinally).
        var laterProvider = new FxRateDatasetKey("twelvedata", Currency.AUD, Currency.CAD, DataFrequency.Tick);
        basis.CompareTo(laterProvider).Should().BeLessThan(0);

        // Same Provider: BaseCurrency dominates QuoteCurrency/Frequency. AUD(36) < EUR(978).
        var sameProviderEarlierBase = new FxRateDatasetKey("frankfurter", Currency.AUD, Currency.JPY, DataFrequency.Tick);
        basis.CompareTo(sameProviderEarlierBase).Should().BeGreaterThan(0);

        // Same Provider+BaseCurrency: QuoteCurrency dominates Frequency. CAD(124) < USD(840).
        var sameToQuoteEarlier = new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.CAD, DataFrequency.Tick);
        basis.CompareTo(sameToQuoteEarlier).Should().BeGreaterThan(0);

        // Same Provider+Base+Quote: Frequency (lowest precedence) breaks the final tie. Tick(0) < Daily(2).
        var sameToFrequency = new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.USD, DataFrequency.Tick);
        basis.CompareTo(sameToFrequency).Should().NotBe(0);
    }

    [Fact]
    public void FxRate_ByteLex_SignAgreesWithCompareTo()
    {
        var keys = new[]
        {
            new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.USD, DataFrequency.Daily),
            new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.GBP, DataFrequency.Daily),
            new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.USD, DataFrequency.Tick),
            new FxRateDatasetKey("frankfurter", Currency.GBP, Currency.USD, DataFrequency.Daily),
            new FxRateDatasetKey("boc", Currency.USD, Currency.CAD, DataFrequency.Daily),
        };

        foreach (var a in keys)
        {
            foreach (var b in keys)
            {
                Math.Sign(a.CompareTo(b)).Should().Be(
                    ByteLex(Wire(a), Wire(b)),
                    "ordered-wire byte-lex must sign-agree with CompareTo for {0} vs {1}",
                    a,
                    b);
            }
        }
    }

    [Fact]
    public void FxRate_SamePrefix_RangeIsContiguous()
    {
        // Three quote currencies sharing (Provider, BaseCurrency) ordered by ISO code:
        // CAD(124) < JPY(392) < USD(840). Nothing with a different (Provider, BaseCurrency)
        // prefix can fall inside the bracket.
        var low = new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.CAD, DataFrequency.Daily);
        var mid = new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.JPY, DataFrequency.Daily);
        var high = new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.USD, DataFrequency.Daily);
        // GBP(826) < EUR(978), so a GBP-base key sorts entirely BEFORE the EUR band.
        var otherPrefix = new FxRateDatasetKey("frankfurter", Currency.GBP, Currency.USD, DataFrequency.Daily);

        low.CompareTo(mid).Should().BeLessThan(0);
        mid.CompareTo(high).Should().BeLessThan(0);
        otherPrefix.CompareTo(low).Should().BeLessThan(0);
    }
}
