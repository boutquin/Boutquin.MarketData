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
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.Storage.Domain.Helpers;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

// M-CR Fix A — FuturesSettlement (and its value-object wrappers FuturesProductCode / ContractMonth)
// become tagged-wire serializable so FuturesSettlement satisfies ISerializable<FuturesSettlement>
// and is eligible for ITimeSeriesStore / the M-4' row-format store registration.
//
// These tests pin:
//   • FuturesProductCode + ContractMonth tagged-wire round-trip (Serialize → Deserialize identity).
//   • FuturesSettlement tagged-wire round-trip — both ImpliedRate = null (stays null) and non-null.
//   • Validation preserved across the positional reshape (invalid wrapper construction still throws).
//   • Forward compatibility (AC-12 mirror): an unknown higher tag on a FuturesSettlement payload is
//     skipped on read and the known fields survive.
public sealed class FuturesSettlementRoundTripTests
{
    private static readonly DateOnly s_date = new(2025, 6, 20);
    private static readonly FuturesProductCode s_sr3 = new("SR3");
    private static readonly ContractMonth s_jun2025 = new(2025, 6);

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(10000, 1, false)]
    [InlineData(2026, 0, false)]
    [InlineData(2026, 13, false)]
    [InlineData(0, 1, true)]
    [InlineData(10000, 1, true)]
    [InlineData(2026, 0, true)]
    [InlineData(2026, 13, true)]
    public void ContractMonth_rejects_invalid_init_assignments(int year, int month, bool initializer)
    {
        Action assign = () => _ = initializer
            ? new ContractMonth { Year = year, Month = month }
            : new ContractMonth(2026, 1) with { Year = year, Month = month };
        assign.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" \t", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" \t", true)]
    public void FuturesProductCode_rejects_invalid_init_assignments(string? value, bool initializer)
    {
        Action assign = () => _ = initializer
            ? new FuturesProductCode { Value = value! }
            : new FuturesProductCode("SR3") with { Value = value! };
        assign.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("month")]
    [InlineData("product")]
    [InlineData("nested-month")]
    [InlineData("nested-product")]
    [InlineData("partial-month")]
    public void Invalid_default_wrapper_writes_no_bytes(string shape)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(42);
        Action write = () =>
        {
            switch (shape)
            {
                case "month":
                    default(ContractMonth).Serialize(stream);
                    break;
                case "product":
                    default(FuturesProductCode).Serialize(stream);
                    break;
                case "nested-month":
                    new FuturesSettlement(s_date, s_sr3, default, 95m, null).Serialize(stream);
                    break;
                case "nested-product":
                    new FuturesSettlement(s_date, default, s_jun2025, 95m, null).Serialize(stream);
                    break;
                case "partial-month":
                    new ContractMonth { Year = 2026 }.Serialize(stream);
                    break;
            }
        };
        write.Should().Throw<ArgumentException>();
        stream.ToArray().Should().Equal(42);
    }

    [Fact]
    public void Valid_init_updates_roundtrip_and_default_rendering_stays_stable()
    {
        var month = new ContractMonth(2026, 1) with { Year = 2030, Month = 12 };
        var code = new FuturesProductCode("SR3") with { Value = "es" };
        Roundtrip(month, ContractMonth.Deserialize).Should().Be(month);
        Roundtrip(code, FuturesProductCode.Deserialize).Should().Be(code);
        var initialized = new ContractMonth { Year = 2026, Month = 6 };
        Roundtrip(initialized, ContractMonth.Deserialize).Should().Be(initialized);
        default(ContractMonth).ToString().Should().Be("0000-00");
        default(FuturesProductCode).ToString().Should().BeEmpty();
    }

    [Fact]
    public void FuturesProductCode_TaggedWire_RoundTrips()
    {
        var original = new FuturesProductCode("SR3");

        Roundtrip(original, FuturesProductCode.Deserialize).Should().Be(original);
    }

    [Fact]
    public void ContractMonth_TaggedWire_RoundTrips()
    {
        var original = new ContractMonth(2026, 12);

        var round = Roundtrip(original, ContractMonth.Deserialize);

        round.Should().Be(original);
        round.Year.Should().Be(2026);
        round.Month.Should().Be(12);
    }

    [Fact]
    public void FuturesSettlement_TaggedWire_RoundTrips_WithImpliedRate()
    {
        var original = new FuturesSettlement(s_date, s_sr3, s_jun2025, 95.50m, 4.50m);

        Roundtrip(original, FuturesSettlement.Deserialize).Should().Be(original);
    }

    [Fact]
    public void FuturesSettlement_TaggedWire_RoundTrips_WithNullImpliedRate()
    {
        var original = new FuturesSettlement(s_date, s_sr3, s_jun2025, 95.50m, null);

        var round = Roundtrip(original, FuturesSettlement.Deserialize);

        round.Should().Be(original);
        round.ImpliedRate.Should().BeNull();
    }

    [Fact]
    public void ContractMonth_Construction_RejectsInvalidMonth_AfterReshape()
    {
        // Validation preserved across the positional reshape — out-of-range month still throws.
        Action act = () => _ = new ContractMonth(2025, 13);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FuturesProductCode_Construction_RejectsWhitespace_AfterReshape()
    {
        // Validation preserved across the positional reshape — whitespace value still throws.
        Action act = () => _ = new FuturesProductCode("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FuturesSettlement_UnknownHigherTag_IsSkippedOnRead()
    {
        // AC-12 mirror: a future writer adds a higher-tag property; a 2.0 reader skips the unknown
        // tag and returns the record with its known fields intact.
        var original = new FuturesSettlement(s_date, s_sr3, s_jun2025, 95.50m, 4.50m);

        var clean = SerializeToBytes(original);
        var withUnknownTag = AppendUnknownVarintField(clean, tag: 99, value: 123_456);

        withUnknownTag.Length.Should().BeGreaterThan(clean.Length);

        using var ms = new MemoryStream(withUnknownTag);
        var decoded = FuturesSettlement.Deserialize(ms);

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
