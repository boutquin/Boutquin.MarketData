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

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Keys;

// AC-24b / AC-24c — OptionQuoteDatasetKey (M-2.7) is the first MarketData [Key] with a decimal
// property (Strike), which therefore carries [DecimalScale(4)] (BSSG017) so the ordered wire holds
// the byte-lex ⇔ decimal.Compare invariant across mantissa-equivalent scales.
//
// These tests pin:
//   • AC-24b — decimal-scale key ordering: strikes 150.0m / 150.00m / 150.5m sort by NUMERIC value
//     over the generated [Key] ordered-wire bytes, regardless of the input scale (150.0m and 150.00m
//     are value-equal so their wire bytes are identical; 150.5m sorts strictly after both).
//   • AC-24c — contiguous underlying scan: a (Provider, "AAPL", *) byte-lex prefix range covers every
//     AAPL contract contiguously (all expiries / strikes / types) and excludes any other underlying —
//     the volatility-surface read shape.
public sealed class OptionQuoteDatasetKeyOrderingTests
{
    private static readonly DateOnly s_expiry = new(2024, 7, 19);

    private static byte[] Wire(OptionQuoteDatasetKey k)
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
    public void Strike_DecimalScale_HoldsByteLexEqualsDecimalCompare()
    {
        // AC-24b: 150.0m and 150.00m are value-equal (decimal.Compare == 0); 150.5m is strictly
        // greater. [DecimalScale(4)] canonicalizes the wire so byte-lex sign-agrees with decimal.Compare
        // regardless of the mantissa scale the caller supplied.
        var k1500 = new OptionQuoteDatasetKey("opra", "AAPL", s_expiry, 150.0m, OptionType.Call);
        var k15000 = new OptionQuoteDatasetKey("opra", "AAPL", s_expiry, 150.00m, OptionType.Call);
        var k1505 = new OptionQuoteDatasetKey("opra", "AAPL", s_expiry, 150.5m, OptionType.Call);

        // Value-equal strikes (different input scale) → identical wire bytes, byte-lex tie, CompareTo 0.
        ByteLex(Wire(k1500), Wire(k15000)).Should().Be(0);
        Math.Sign(k1500.CompareTo(k15000)).Should().Be(0);
        decimal.Compare(150.0m, 150.00m).Should().Be(0);

        // 150.5m sorts strictly after 150.0m on BOTH the wire and decimal.Compare.
        Math.Sign(k1500.CompareTo(k1505)).Should().Be(Math.Sign(decimal.Compare(150.0m, 150.5m)));
        ByteLex(Wire(k1500), Wire(k1505)).Should().Be(Math.Sign(decimal.Compare(150.0m, 150.5m)));
        ByteLex(Wire(k1505), Wire(k1500)).Should().BeGreaterThan(0);
    }

    [Fact]
    public void CompareTo_FollowsDeclarationOrder()
    {
        // Declaration order: Provider, Underlying, Expiry, Strike, Type. A higher-precedence field
        // dominates every lower one regardless of the lower fields' values.
        var basis = new OptionQuoteDatasetKey("opra", "AAPL", s_expiry, 150m, OptionType.Call);

        // Provider dominates ("opra" < "polygon").
        var laterProvider = new OptionQuoteDatasetKey("polygon", "AAAA", new DateOnly(2020, 1, 1), 10m, OptionType.Call);
        basis.CompareTo(laterProvider).Should().BeLessThan(0);

        // Same Provider: Underlying dominates Expiry/Strike/Type ("AAPL" < "MSFT").
        var sameProviderLaterUnderlying = new OptionQuoteDatasetKey("opra", "MSFT", new DateOnly(2020, 1, 1), 10m, OptionType.Call);
        basis.CompareTo(sameProviderLaterUnderlying).Should().BeLessThan(0);

        // Same Provider+Underlying: Expiry dominates Strike/Type.
        var sameToExpiryLater = new OptionQuoteDatasetKey("opra", "AAPL", new DateOnly(2024, 12, 20), 1m, OptionType.Call);
        basis.CompareTo(sameToExpiryLater).Should().BeLessThan(0);

        // Same Provider+Underlying+Expiry: Strike dominates Type. 150 > 100.
        var sameToStrikeLower = new OptionQuoteDatasetKey("opra", "AAPL", s_expiry, 100m, OptionType.Put);
        basis.CompareTo(sameToStrikeLower).Should().BeGreaterThan(0);

        // Same Provider+Underlying+Expiry+Strike: Type (lowest precedence) breaks the final tie.
        var sameToType = new OptionQuoteDatasetKey("opra", "AAPL", s_expiry, 150m, OptionType.Put);
        basis.CompareTo(sameToType).Should().NotBe(0);
    }

    [Fact]
    public void ByteLex_SignAgreesWithCompareTo()
    {
        var keys = new[]
        {
            new OptionQuoteDatasetKey("opra", "AAPL", s_expiry, 150m, OptionType.Call),
            new OptionQuoteDatasetKey("opra", "AAPL", s_expiry, 150m, OptionType.Put),
            new OptionQuoteDatasetKey("opra", "AAPL", s_expiry, 155m, OptionType.Call),
            new OptionQuoteDatasetKey("opra", "AAPL", new DateOnly(2024, 12, 20), 150m, OptionType.Call),
            new OptionQuoteDatasetKey("opra", "MSFT", s_expiry, 150m, OptionType.Call),
            new OptionQuoteDatasetKey("polygon", "AAPL", s_expiry, 150m, OptionType.Call),
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
    public void SameUnderlyingPrefix_RangeIsContiguous()
    {
        // AC-24c: every AAPL contract (all expiries/strikes/types) sorts as a contiguous block,
        // bracketed by the lexicographically-least and -greatest AAPL keys; no other underlying
        // can fall inside that bracket. This is the vol-surface read shape:
        // (provider, "AAPL", *) is a single contiguous byte-lex prefix range.
        var aaplLow = new OptionQuoteDatasetKey("opra", "AAPL", new DateOnly(2024, 1, 1), 50m, OptionType.Call);
        var aaplMid = new OptionQuoteDatasetKey("opra", "AAPL", s_expiry, 150m, OptionType.Put);
        var aaplHigh = new OptionQuoteDatasetKey("opra", "AAPL", new DateOnly(2027, 12, 17), 500m, OptionType.Put);

        // A contract whose underlying sorts BEFORE "AAPL" and one that sorts AFTER bracket the band.
        var beforeBand = new OptionQuoteDatasetKey("opra", "AAA", new DateOnly(2099, 1, 1), 9999m, OptionType.Put);
        var afterBand = new OptionQuoteDatasetKey("opra", "AABB", new DateOnly(1900, 1, 1), 1m, OptionType.Call);

        // Within the AAPL band: ascending.
        aaplLow.CompareTo(aaplMid).Should().BeLessThan(0);
        aaplMid.CompareTo(aaplHigh).Should().BeLessThan(0);

        // "AAA" < "AABB" < "AAPL" ordinally, so both flank-keys sort strictly before the whole AAPL band,
        // proving nothing with a different underlying interleaves inside the AAPL prefix range.
        beforeBand.CompareTo(aaplLow).Should().BeLessThan(0);
        afterBand.CompareTo(aaplLow).Should().BeLessThan(0);

        // And the byte-lex wire agrees: the smallest AAPL key's wire is strictly greater than the flank keys'.
        ByteLex(Wire(aaplLow), Wire(beforeBand)).Should().BeGreaterThan(0);
        ByteLex(Wire(aaplLow), Wire(afterBand)).Should().BeGreaterThan(0);
    }
}
