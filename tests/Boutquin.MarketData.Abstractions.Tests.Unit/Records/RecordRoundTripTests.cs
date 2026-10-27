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
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.Storage.Domain.Helpers;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

// AC-12 — tagged-wire serialization round-trip + schema-evolution unknown-tag skip.
//
// Every record annotated [StorageSerializable] emits ISerializable<T> (Serialize(Stream) +
// static Deserialize(Stream)) via the Storage 2.0 source generator. These tests pin:
//
//   • Round-trip identity: Serialize → Deserialize reconstructs every record value-equal,
//     including the row opt-out records (DividendPayment is row opt-out but its fields are all
//     serializable). FuturesSettlement is also tagged-wire serializable as of M-CR Fix A — its
//     dedicated round-trip + forward-compat tests live in FuturesSettlementRoundTripTests.
//   • Forward compatibility (AC-12): a 2.0 reader decoding a payload written by a future binary
//     that added a higher-tag property skips the unknown tag and returns the record with the
//     known fields intact — no exception.
public sealed class RecordRoundTripTests
{
    private static readonly DateOnly s_date = new(2024, 6, 13);

    [Fact]
    public void Bar_TaggedWire_RoundTrips()
    {
        var original = new Bar(s_date, 100.1m, 101.2m, 99.3m, 100.7m, 100.5m, 1_234_567L);

        Roundtrip(original, Bar.Deserialize).Should().Be(original);
    }

    [Fact]
    public void FxRate_TaggedWire_RoundTrips()
    {
        // FxRate slimmed to (Date, Rate) per M-2.6 — currency identity lives in FxRateDatasetKey.
        var original = new FxRate(s_date, 1.0845m);

        Roundtrip(original, FxRate.Deserialize).Should().Be(original);
    }

    [Fact]
    public void ScalarObservation_TaggedWire_RoundTrips()
    {
        var original = new ScalarObservation(s_date, 0.0451m, "decimal");

        Roundtrip(original, ScalarObservation.Deserialize).Should().Be(original);
    }

    [Fact]
    public void FactorObservation_TaggedWire_RoundTrips()
    {
        var original = new FactorObservation(s_date, "Mkt-RF", 0.0039m);

        Roundtrip(original, FactorObservation.Deserialize).Should().Be(original);
    }

    [Fact]
    public void YieldCurveQuote_TaggedWire_RoundTrips()
    {
        var original = new YieldCurveQuote(s_date, "10Y", 0.0451m);

        Roundtrip(original, YieldCurveQuote.Deserialize).Should().Be(original);
    }

    [Fact]
    public void DividendPayment_TaggedWire_RoundTrips()
    {
        var original = new DividendPayment(s_date, 0.24m, Currency.USD, DividendType.UsOrdinary);

        Roundtrip(original, DividendPayment.Deserialize).Should().Be(original);
    }

    [Fact]
    public void DividendPayment_TaggedWire_RoundTrips_ViaExplicitDate()
    {
        // DividendPayment's sort-defining date is ExDate, surfaced through IHasDate.Date.
        var original = new DividendPayment(s_date, 1.50m, Currency.CAD, DividendType.CanadianEligible);

        var round = Roundtrip(original, DividendPayment.Deserialize);

        round.Should().Be(original);
        ((Boutquin.Storage.Domain.Interfaces.TimeSeries.IHasDate)round).Date.Should().Be(s_date);
    }

    [Fact]
    public void ScalarObservation_UnknownHigherTag_IsSkippedOnRead()
    {
        // AC-12: a future writer adds a property with a tag higher than any current tag. A 2.0
        // reader must skip the unknown tag and return the record with its known fields intact.
        var original = new ScalarObservation(s_date, 0.0451m, "decimal");

        var clean = SerializeToBytes(original);
        var withUnknownTag = AppendUnknownVarintField(clean, tag: 99, value: 123_456);

        // The augmented payload is strictly larger — the unknown tag-99 field is really present, so
        // a passing decode below can only mean the reader skipped it (not that the field was absent).
        withUnknownTag.Length.Should().BeGreaterThan(clean.Length);

        using var ms = new MemoryStream(withUnknownTag);
        var decoded = ScalarObservation.Deserialize(ms);

        decoded.Should().Be(original);
    }

    [Fact]
    public void Bar_UnknownHigherTag_IsSkippedOnRead()
    {
        var original = new Bar(s_date, 100.1m, 101.2m, 99.3m, 100.7m, 100.5m, 1_234_567L);

        var clean = SerializeToBytes(original);
        var withUnknownTag = AppendUnknownVarintField(clean, tag: 42, value: 7);

        withUnknownTag.Length.Should().BeGreaterThan(clean.Length);

        using var ms = new MemoryStream(withUnknownTag);
        var decoded = Bar.Deserialize(ms);

        decoded.Should().Be(original);
    }

    private static TRecord Roundtrip<TRecord>(TRecord value, Func<Stream, TRecord> deserialize)
        where TRecord : Boutquin.Storage.Domain.Interfaces.ISerializable<TRecord>
    {
        using var ms = new MemoryStream();
        value.Serialize(ms);
        ms.Position = 0;
        return deserialize(ms);
    }

    private static byte[] SerializeToBytes<TRecord>(TRecord value)
        where TRecord : Boutquin.Storage.Domain.Interfaces.ISerializable<TRecord>
    {
        using var ms = new MemoryStream();
        value.Serialize(ms);
        return ms.ToArray();
    }

    // Reframe a serialized record's [length-prefix | payload] so the payload carries one extra
    // unknown Varint field at <paramref name="tag"/>, rewriting the ULEB128 length prefix. Uses the
    // same VarintHelpers framing the generator emits, so the appended field is wire-valid and the
    // reader exercises its real unknown-tag SkipField path.
    private static byte[] AppendUnknownVarintField(byte[] framed, int tag, long value)
    {
        using var src = new MemoryStream(framed);
        var payloadLength = (int)VarintHelpers.ReadVarUInt64(src);
        var payload = new byte[payloadLength];
        VarintHelpers.ReadExact(src, payload);

        using var newPayload = new MemoryStream();
        newPayload.Write(payload, 0, payload.Length);
        VarintHelpers.WriteTag(newPayload, tag, WireType.Varint);
        VarintHelpers.WriteZigZagInt64(newPayload, value);
        var newPayloadBytes = newPayload.ToArray();

        using var outStream = new MemoryStream();
        VarintHelpers.WriteVarUInt64(outStream, (ulong)newPayloadBytes.Length);
        outStream.Write(newPayloadBytes, 0, newPayloadBytes.Length);
        return outStream.ToArray();
    }
}
