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
using Boutquin.MarketData.Abstractions.Records;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

// AC-23 (M-2.6 FX normalization, §2.5.1 / Q15). The FxRate record is slimmed to (Date, Rate):
// the currency identity that previously lived in the body now lives ONLY in FxRateDatasetKey,
// matching the spec thesis "don't store identity in payload; let the key carry identity".
//
//   • AC-23a — slimmed round-trip: the (Date, Rate) record round-trips through the generated
//     tagged-wire codec byte/value-identical, exercised exactly as RecordRoundTripTests does for
//     the other [StorageSerializable] records (Serialize → Deserialize is value-equal).
//   • AC-23b — no-denormalization regression: typeof(FxRate) has NO BaseCurrency / QuoteCurrency
//     property (reflection); the denormalization is gone.
//   • AC-23d — ergonomics intact: FxPair.MarketConvention() / Invert() still compile + behave at
//     the call site (the pair stays the caller-facing construction ergonomic).
public sealed class FxRateReshapeRoundTripTests
{
    private static readonly DateOnly s_date = new(2024, 6, 13);

    [Fact]
    public void FxRate_SlimmedShape_TaggedWire_RoundTrips()
    {
        // AC-23a: a slimmed FxRate(Date, Rate) round-trips through the generated tagged-wire codec
        // value-identical, with no currency fields on the body (exercised exactly as
        // RecordRoundTripTests does for the other [StorageSerializable] records).
        var original = new FxRate(s_date, 1.0845m);

        using var ms = new MemoryStream();
        original.Serialize(ms);
        ms.Position = 0;
        var decoded = FxRate.Deserialize(ms);

        decoded.Should().Be(original);
        decoded.Date.Should().Be(s_date);
        decoded.Rate.Should().Be(1.0845m);
    }

    [Fact]
    public void FxRate_HasNoCurrencyProperties_DenormalizationIsGone()
    {
        // AC-23b: the identity-in-payload denormalization is removed — the currencies live only
        // in FxRateDatasetKey now.
        var t = typeof(FxRate);

        t.GetProperty("BaseCurrency").Should().BeNull(
            "BaseCurrency moved to FxRateDatasetKey (M-2.6 / Q15) — it must not remain on the body");
        t.GetProperty("QuoteCurrency").Should().BeNull(
            "QuoteCurrency moved to FxRateDatasetKey (M-2.6 / Q15) — it must not remain on the body");
    }

    [Fact]
    public void FxPair_Ergonomics_StillCompileAtCallSite()
    {
        // AC-23d: FxPair remains the caller-facing construction ergonomic; MarketConvention()
        // and Invert() still compile and behave. The dataset-key builder expands the pair into
        // the two Currency key columns.
        var pair = new FxPair(Currency.USD, Currency.EUR);

        var convention = pair.MarketConvention();
        convention.BaseCurrency.Should().Be(Currency.EUR);
        convention.QuoteCurrency.Should().Be(Currency.USD);

        var inverted = pair.Invert();
        inverted.BaseCurrency.Should().Be(Currency.EUR);
        inverted.QuoteCurrency.Should().Be(Currency.USD);
    }
}
