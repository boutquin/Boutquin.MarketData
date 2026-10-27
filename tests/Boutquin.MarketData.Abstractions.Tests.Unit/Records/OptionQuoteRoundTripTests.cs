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

using Boutquin.MarketData.Abstractions.Records;
using Boutquin.Storage.Domain.Helpers;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

// AC-24a / AC-24d — OptionQuote (M-2.7) brought into scope, slimmed to (Date, Bid?, Ask?, LastPrice?,
// ImpliedVolatility?, OpenInterest, Volume) with the contract identity hoisted into
// OptionQuoteDatasetKey. The record takes the row opt-out ([StorageSerializable] only) because its
// four nullable decimals are BSSG020 columnar hazards and nullability is semantically essential
// (sparse quotes). These tests pin:
//   • AC-24a — nullable round-trip: Serialize → Deserialize reconstructs every record value-equal,
//     with null Bid/Ask preserved as null (absent tag ⇒ null on the tagged-wire row path).
//   • AC-24d — forward compatibility (AC-12 mirror for OptionQuote): a 2.0 reader decoding a payload
//     written by a future binary that added a higher-tag property skips the unknown tag and returns
//     the record with the known fields intact.
public sealed class OptionQuoteRoundTripTests
{
    private static readonly DateOnly s_date = new(2024, 6, 15);

    [Fact]
    public void OptionQuote_TaggedWire_RoundTrips_AllFieldsPresent()
    {
        var original = new OptionQuote(s_date, 5.20m, 5.40m, 5.30m, 0.25m, 15_000L, 2_500L);

        Roundtrip(original, OptionQuote.Deserialize).Should().Be(original);
    }

    [Fact]
    public void OptionQuote_TaggedWire_RoundTrips_NullBidAskStayNull()
    {
        // AC-24a: a sparse quote with no Bid/Ask/LastPrice/IV — every absent nullable tag must
        // decode back to null, not to default(decimal) = 0m.
        var original = new OptionQuote(s_date, null, null, null, null, 0L, 0L);

        var round = Roundtrip(original, OptionQuote.Deserialize);

        round.Should().Be(original);
        round.Bid.Should().BeNull();
        round.Ask.Should().BeNull();
        round.LastPrice.Should().BeNull();
        round.ImpliedVolatility.Should().BeNull();
    }

    [Fact]
    public void OptionQuote_TaggedWire_RoundTrips_MixedNullability()
    {
        // Some present, some null — the present ones survive, the null ones stay null.
        var original = new OptionQuote(s_date, 5.20m, null, null, 0.31m, 100L, 0L);

        var round = Roundtrip(original, OptionQuote.Deserialize);

        round.Should().Be(original);
        round.Bid.Should().Be(5.20m);
        round.Ask.Should().BeNull();
        round.LastPrice.Should().BeNull();
        round.ImpliedVolatility.Should().Be(0.31m);
    }

    [Fact]
    public void OptionQuote_UnknownHigherTag_IsSkippedOnRead()
    {
        // AC-24d (AC-12 mirror): a future writer adds a higher-tag property; a 2.0 reader skips the
        // unknown tag and returns the record with its known fields intact.
        var original = new OptionQuote(s_date, 5.20m, 5.40m, 5.30m, 0.25m, 15_000L, 2_500L);

        var clean = SerializeToBytes(original);
        var withUnknownTag = AppendUnknownVarintField(clean, tag: 99, value: 123_456);

        withUnknownTag.Length.Should().BeGreaterThan(clean.Length);

        using var ms = new MemoryStream(withUnknownTag);
        var decoded = OptionQuote.Deserialize(ms);

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
