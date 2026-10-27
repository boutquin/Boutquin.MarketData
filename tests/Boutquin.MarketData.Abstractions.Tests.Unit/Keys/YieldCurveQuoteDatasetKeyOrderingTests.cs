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

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Keys;

// 8a — YieldCurveQuoteDatasetKey(Provider, CurveId, Tenor) ordered-wire byte-lex contract.
//
// Tenor is the per-node discriminant hoisted into the key so curve nodes of one (Provider, CurveId)
// do not collide on the store's (DatasetKey, Date) key. These tests pin that composite ordering
// follows declaration order (Provider → CurveId → Tenor) and that the serialized bytes order the
// same way, so one tenor's history is a contiguous (Provider, CurveId, Tenor) prefix scan.
public sealed class YieldCurveQuoteDatasetKeyOrderingTests
{
    private static byte[] Wire(YieldCurveQuoteDatasetKey k)
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
    public void CompareTo_FollowsDeclarationOrder()
    {
        var basis = new YieldCurveQuoteDatasetKey("boc", "CAD-ZERO", "5Y");

        // Provider dominates CurveId/Tenor.
        basis.CompareTo(new YieldCurveQuoteDatasetKey("ustreasury", "AAA", "1M")).Should().BeLessThan(0);

        // Same Provider: CurveId dominates Tenor.
        basis.CompareTo(new YieldCurveQuoteDatasetKey("boc", "CAD-ZZZ", "1M")).Should().BeLessThan(0);

        // Same Provider+CurveId: Tenor breaks the tie ("3M" < "5Y" by byte-lex on the string).
        new YieldCurveQuoteDatasetKey("boc", "CAD-ZERO", "3M")
            .CompareTo(basis).Should().BeLessThan(0);
    }

    [Fact]
    public void OneCurve_TenorsAreContiguous_AndByteLexAgrees()
    {
        var nodes = new[]
        {
            new YieldCurveQuoteDatasetKey("boc", "CAD-ZERO", "1Y"),
            new YieldCurveQuoteDatasetKey("boc", "CAD-ZERO", "3M"),
            new YieldCurveQuoteDatasetKey("boc", "CAD-ZERO", "5Y"),
            new YieldCurveQuoteDatasetKey("boc", "USD-OIS", "3M"),
            new YieldCurveQuoteDatasetKey("ustreasury", "CAD-ZERO", "3M"),
        };

        foreach (var a in nodes)
        {
            foreach (var b in nodes)
            {
                Math.Sign(a.CompareTo(b)).Should().Be(
                    ByteLex(Wire(a), Wire(b)),
                    "ordered-wire byte-lex must sign-agree with CompareTo for {0} vs {1}",
                    a,
                    b);
            }
        }

        // Every "boc/CAD-ZERO" node (any tenor) sorts before every "boc/USD-OIS" node — the
        // CurveId precedes Tenor, so one curve's tenors form a contiguous range no other curve's
        // nodes interleave.
        var cadZeroNodes = new[] { "1M", "3M", "1Y", "5Y", "10Y", "30Y" };
        var usdOisNodes = new[] { "1M", "3M", "1Y", "5Y", "10Y", "30Y" };
        foreach (var c in cadZeroNodes)
        {
            foreach (var u in usdOisNodes)
            {
                new YieldCurveQuoteDatasetKey("boc", "CAD-ZERO", c)
                    .CompareTo(new YieldCurveQuoteDatasetKey("boc", "USD-OIS", u))
                    .Should().BeLessThan(0);
            }
        }
    }
}
