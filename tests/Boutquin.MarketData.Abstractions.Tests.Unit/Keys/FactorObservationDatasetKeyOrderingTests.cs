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
using Boutquin.MarketData.Abstractions.Requests;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Keys;

// 8b — FactorObservationDatasetKey(Provider, DatasetId, Frequency, FactorName) ordered-wire
// byte-lex contract. FactorName is the per-row discriminant added so factors of one date do not
// collide on the store's (DatasetKey, Date) key. These tests pin that composite ordering follows
// declaration order (Provider → DatasetId → Frequency → FactorName) and that the serialized bytes
// order the same way, so one factor's history is a contiguous prefix scan and a full factor-set
// read is a (Provider, DatasetId, Frequency, *) range.
public sealed class FactorObservationDatasetKeyOrderingTests
{
    private static byte[] Wire(FactorObservationDatasetKey k)
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
        var basis = new FactorObservationDatasetKey("famafrench", "Research", DataFrequency.Daily, "Mkt-RF");

        // Provider dominates everything below it.
        basis.CompareTo(new FactorObservationDatasetKey("zzz", "AAA", DataFrequency.Tick, "AAA"))
            .Should().BeLessThan(0);

        // Same Provider: DatasetId dominates Frequency/FactorName.
        basis.CompareTo(new FactorObservationDatasetKey("famafrench", "Zesearch", DataFrequency.Tick, "AAA"))
            .Should().BeLessThan(0);

        // Same Provider+DatasetId: Frequency dominates FactorName. Tick(0) < Daily(2).
        new FactorObservationDatasetKey("famafrench", "Research", DataFrequency.Tick, "ZZZ")
            .CompareTo(basis).Should().BeLessThan(0);

        // Same Provider+DatasetId+Frequency: FactorName breaks the final tie ("HML" < "Mkt-RF").
        new FactorObservationDatasetKey("famafrench", "Research", DataFrequency.Daily, "HML")
            .CompareTo(basis).Should().BeLessThan(0);
    }

    [Fact]
    public void OneFactor_HistoryIsContiguous_AndByteLexAgrees()
    {
        var keys = new[]
        {
            new FactorObservationDatasetKey("famafrench", "Research", DataFrequency.Daily, "HML"),
            new FactorObservationDatasetKey("famafrench", "Research", DataFrequency.Daily, "Mkt-RF"),
            new FactorObservationDatasetKey("famafrench", "Research", DataFrequency.Daily, "SMB"),
            new FactorObservationDatasetKey("famafrench", "Research", DataFrequency.Monthly, "HML"),
            new FactorObservationDatasetKey("aqr", "Research", DataFrequency.Daily, "HML"),
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
}
