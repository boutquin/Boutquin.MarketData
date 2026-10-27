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

// M-CR dataset-key contract tests (feature-time-series-store-adoption.md §2.5 / §3.1.1 PATCH).
//
// Each DatasetKey is a readonly partial record struct annotated [Key]. Under Storage 2.0 the
// generator emits ISerializable<T>, IComparable<T>, and IEquatable<T> from per-property [Tag(N)]
// annotations, and native enums (DataFrequency / Currency / ExchangeCode?) are first-class key
// columns (the 1.x all-string workaround is retired; Provider stays string — open registry). These
// tests assert:
//
//   • The runtime-castable interfaces are present.
//   • Lex ordering holds: fields compare left-to-right, so a range scan of the time-series store
//     for one prefix triple hits a contiguous key range.
//   • Equality is value-based (record struct auto-emits IEquatable).
//   • Serialize/Deserialize round-trips preserve every field bit-identically.
public sealed class DatasetKeyTests
{
    [Fact]
    public void PriceBarDatasetKey_ImplementsContract()
    {
        var key = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", ExchangeCode.XNYS);

        // Note: ISerializable<T> carries a `static abstract Deserialize` — types cannot
        // be reference-cast to interfaces with static-abstract members, so we verify
        // the contract by exercising Serialize / Deserialize directly below. Here we
        // only assert the runtime-castable interfaces.
        key.Should().BeAssignableTo<IComparable<PriceBarDatasetKey>>();
        key.Should().BeAssignableTo<IEquatable<PriceBarDatasetKey>>();
    }

    [Fact]
    public void PriceBarDatasetKey_Equality_ByValue()
    {
        var a = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", ExchangeCode.XNYS);
        var b = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", ExchangeCode.XNYS);
        var c = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "BND", ExchangeCode.XNYS);

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    [Fact]
    public void PriceBarDatasetKey_Ordering_IsLexicographic()
    {
        // Provider first → tiingo < twelvedata regardless of downstream fields.
        var tg = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "ZZZZ", ExchangeCode.XNYS);
        var td = new PriceBarDatasetKey("twelvedata", DataFrequency.Daily, "AAAA", ExchangeCode.XNYS);
        tg.CompareTo(td).Should().BeLessThan(0);

        // Same provider → Frequency breaks the tie. DataFrequency enum values order by declaration:
        // Tick(0) < Intraday(1) < Daily(2) < Weekly(3) < Monthly(4), so Intraday < Daily.
        var intraday = new PriceBarDatasetKey("tiingo", DataFrequency.Intraday, "AGG", ExchangeCode.XNYS);
        var daily = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", ExchangeCode.XNYS);
        intraday.CompareTo(daily).Should().BeLessThan(0);

        // Same provider+freq → Ticker breaks the tie.
        var agg = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", ExchangeCode.XNYS);
        var bnd = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "BND", ExchangeCode.XNYS);
        agg.CompareTo(bnd).Should().BeLessThan(0);
    }

    [Fact]
    public void PriceBarDatasetKey_Serialization_RoundTrips()
    {
        var original = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", ExchangeCode.XNYS);

        using var ms = new MemoryStream();
        original.Serialize(ms);
        ms.Position = 0;
        var round = PriceBarDatasetKey.Deserialize(ms);

        round.Should().Be(original);
    }

    [Fact]
    public void PriceBarDatasetKey_Serialization_RoundTrips_NullExchange()
    {
        // Exchange is a nullable enum — the ordered wire encodes a 1-byte null discriminator.
        var original = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", Exchange: null);

        using var ms = new MemoryStream();
        original.Serialize(ms);
        ms.Position = 0;
        var round = PriceBarDatasetKey.Deserialize(ms);

        round.Should().Be(original);
        round.Exchange.Should().BeNull();
    }

    [Fact]
    public void ScalarObservationDatasetKey_Equality_Ordering_Roundtrip()
    {
        var a = new ScalarObservationDatasetKey("fred", "DGS10");
        var b = new ScalarObservationDatasetKey("fred", "DGS10");
        var c = new ScalarObservationDatasetKey("fred", "GDP");

        a.Should().Be(b);
        a.CompareTo(c).Should().BeLessThan(0);

        using var ms = new MemoryStream();
        a.Serialize(ms);
        ms.Position = 0;
        ScalarObservationDatasetKey.Deserialize(ms).Should().Be(a);
    }

    [Fact]
    public void FxRateDatasetKey_Equality_Ordering_Roundtrip()
    {
        // Enums order on the ordered wire by their UNDERLYING ISO-4217 integer value, not by name:
        // CAD = 124 < EUR = 978, so a CAD-base key sorts before an EUR-base key.
        var cadusd = new FxRateDatasetKey("frankfurter", Currency.CAD, Currency.USD, DataFrequency.Daily);
        var eurusd = new FxRateDatasetKey("frankfurter", Currency.EUR, Currency.USD, DataFrequency.Daily);

        cadusd.CompareTo(eurusd).Should().BeLessThan(0);

        using var ms = new MemoryStream();
        eurusd.Serialize(ms);
        ms.Position = 0;
        FxRateDatasetKey.Deserialize(ms).Should().Be(eurusd);
    }

    [Fact]
    public void FuturesSettlementDatasetKey_Equality_Ordering_Roundtrip()
    {
        var h1 = new FuturesSettlementDatasetKey("cme", "SR3", "2026-03");
        var h2 = new FuturesSettlementDatasetKey("cme", "SR3", "2026-06");

        h1.CompareTo(h2).Should().BeLessThan(0);

        using var ms = new MemoryStream();
        h1.Serialize(ms);
        ms.Position = 0;
        FuturesSettlementDatasetKey.Deserialize(ms).Should().Be(h1);
    }

    [Fact]
    public void FactorObservationDatasetKey_Equality_Ordering_Roundtrip()
    {
        // 8b: the key now carries FactorName as its fourth, per-factor discriminant.
        var f3 = new FactorObservationDatasetKey("famafrench", "3Factors", DataFrequency.Daily, "Mkt-RF");
        var f5 = new FactorObservationDatasetKey("famafrench", "5Factors", DataFrequency.Daily, "Mkt-RF");

        f3.CompareTo(f5).Should().BeLessThan(0);

        using var ms = new MemoryStream();
        f3.Serialize(ms);
        ms.Position = 0;
        FactorObservationDatasetKey.Deserialize(ms).Should().Be(f3);
    }

    [Fact]
    public void DividendPaymentDatasetKey_Equality_Ordering_Roundtrip()
    {
        var aapl = new DividendPaymentDatasetKey("tiingo", "AAPL");
        var msft = new DividendPaymentDatasetKey("tiingo", "MSFT");

        aapl.CompareTo(msft).Should().BeLessThan(0);

        using var ms = new MemoryStream();
        aapl.Serialize(ms);
        ms.Position = 0;
        DividendPaymentDatasetKey.Deserialize(ms).Should().Be(aapl);
    }

    [Fact]
    public void DividendPaymentDatasetKey_PreservesLegacyTwoCoordinateWireAndSeparatesVenues()
    {
        const string legacyHex = "7469696E676F00004141504C0000";
        var legacy = new DividendPaymentDatasetKey("tiingo", "AAPL");

        using var legacyBytes = new MemoryStream();
        legacy.Serialize(legacyBytes);
        Convert.ToHexString(legacyBytes.ToArray()).Should().Be(legacyHex);

        legacyBytes.Position = 0;
        DividendPaymentDatasetKey.Deserialize(legacyBytes).Should().Be(legacy);

        var nyse = new DividendPaymentDatasetKey("tiingo", "SHOP", ExchangeCode.XNYS);
        var toronto = new DividendPaymentDatasetKey("tiingo", "SHOP", ExchangeCode.XTSE);
        nyse.Should().NotBe(toronto);
        nyse.Ticker.Should().Be("SHOP/XNYS");

        using var venueBytes = new MemoryStream();
        nyse.Serialize(venueBytes);
        venueBytes.Position = 0;
        DividendPaymentDatasetKey.Deserialize(venueBytes).Should().Be(nyse);
    }

    [Fact]
    public void YieldCurveQuoteDatasetKey_Equality_Ordering_Roundtrip()
    {
        // 8a: new key — Tenor is the per-node discriminant.
        var t3m = new YieldCurveQuoteDatasetKey("boc", "CAD-ZERO", "3M");
        var t10y = new YieldCurveQuoteDatasetKey("boc", "CAD-ZERO", "3Y");

        // "10Y"-style labels do not byte-lex by maturity (string ordering), so this test uses
        // labels whose string ordering is unambiguous; full byte-lex contiguity is covered by
        // YieldCurveQuoteDatasetKeyOrderingTests.
        t3m.CompareTo(t10y).Should().BeLessThan(0);

        using var ms = new MemoryStream();
        t3m.Serialize(ms);
        ms.Position = 0;
        YieldCurveQuoteDatasetKey.Deserialize(ms).Should().Be(t3m);
    }
}
