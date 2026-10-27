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
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.Storage.Domain.Helpers;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Records;

// M-1 currency-unification serialization safety (umbrella AC-3 + repo AC-M1-C).
//
// The unification deletes MarketData's 14-member CurrencyCode enum and retypes every currency
// member to the 177-member Boutquin.Domain.Enumerations.Currency. Both enums use IDENTICAL ISO 4217
// numeric backing (USD=840, EUR=978, ...), so the swap MUST be byte-identical on the wire for the
// serialization-critical record types. The Storage source generator encodes an enum purely as its
// underlying (int) value on both wire formats:
//   • tagged-wire records  (DividendPayment): VarintHelpers.WriteZigZagInt64((int)currency)  [Tag 3]
//   • ordered-wire [Key]s  (FxRateDatasetKey): OrderedByteHelpers.WriteInt32((int)currency)  (fixed 4-byte)
// so proving the encoded column equals the currency's ISO 4217 number proves wire-compatibility with
// data written by the old enum (whose members carried the same numbers).
//
// AC-3 is "necessary AND sufficient": round-trip equality PLUS encoded-numeric-equals-ISO identity.
public sealed class CurrencyUnificationSerializationTests
{
    private static readonly DateOnly s_date = new(2024, 6, 13);

    // ---- AC-3: DividendPayment (tagged-wire row, Currency = Tag 3) -------------------------------

    [Theory]
    [InlineData(Currency.USD, 840)]
    [InlineData(Currency.EUR, 978)]
    [InlineData(Currency.GBP, 826)]
    [InlineData(Currency.CAD, 124)]
    [InlineData(Currency.CHF, 756)]
    [InlineData(Currency.JPY, 392)]
    public void DividendPayment_WireEncodesCurrencyColumnAsIso4217Numeric(Currency currency, int isoNumeric)
    {
        var record = new DividendPayment(s_date, 0.24m, currency, DividendType.UsOrdinary);

        var wire = SerializeToBytes(record);

        // The encoded currency column equals the ISO 4217 number — byte-identical to the old enum.
        DecodeTaggedCurrency(wire, currencyTag: 3).Should().Be(isoNumeric);

        // ...and the value survives a full round-trip through the real deserializer.
        Roundtrip(wire, DividendPayment.Deserialize).Currency.Should().Be(currency);
    }

    // ---- AC-3: FxRateDatasetKey ([Key] ordered wire, BaseCurrency + QuoteCurrency columns) -------

    [Theory]
    [InlineData(Currency.EUR, Currency.USD, 978, 840)]
    [InlineData(Currency.GBP, Currency.JPY, 826, 392)]
    [InlineData(Currency.USD, Currency.CAD, 840, 124)]
    [InlineData(Currency.CHF, Currency.EUR, 756, 978)]
    public void FxRateDatasetKey_WireEncodesBothCurrencyColumnsAsIso4217Numerics(
        Currency baseCurrency, Currency quoteCurrency, int baseIso, int quoteIso)
    {
        var key = new FxRateDatasetKey("frankfurter", baseCurrency, quoteCurrency, DataFrequency.Daily);

        var wire = Wire(key);

        var (decodedBase, decodedQuote) = DecodeKeyCurrencyColumns(wire);
        decodedBase.Should().Be(baseIso, because: "the BaseCurrency key column must encode its ISO 4217 number");
        decodedQuote.Should().Be(quoteIso, because: "the QuoteCurrency key column must encode its ISO 4217 number");

        // Full round-trip through the real ordered-key deserializer.
        FxRateDatasetKey.Deserialize(new MemoryStream(wire)).Should().Be(key);
    }

    // ---- Default-shift property tests (AC-M1-C executable verification) --------------------------

    [Fact]
    public void Default_Currency_is_the_Unspecified_zero_sentinel()
    {
        // CurrencyCode had no 0 member; Currency.Unspecified = 0 is the new default. This pins the
        // sentinel so any code relying on "no zero value exists" is forced to reckon with it.
        default(Currency).Should().Be(Currency.Unspecified);
        ((int)Currency.Unspecified).Should().Be(0);
    }

    [Theory]
    [InlineData(Currency.USD)]
    [InlineData(Currency.CAD)]
    [InlineData(Currency.MXN)]
    [InlineData(Currency.GBP)]
    [InlineData(Currency.EUR)]
    [InlineData(Currency.JPY)]
    [InlineData(Currency.CNY)]
    [InlineData(Currency.INR)]
    [InlineData(Currency.AUD)]
    [InlineData(Currency.BRL)]
    [InlineData(Currency.RUB)]
    [InlineData(Currency.KRW)]
    [InlineData(Currency.HKD)]
    [InlineData(Currency.CHF)]
    public void Unspecified_sentinel_is_distinct_from_every_MarketData_currency(Currency real)
    {
        // The zero sentinel can never be confused with a real ISO currency MarketData persists.
        Currency.Unspecified.Should().NotBe(real);
    }

    [Fact]
    public void DividendPayment_with_Unspecified_currency_surfaces_the_zero_sentinel_explicitly()
    {
        // A zero on the wire decodes to the explicit Unspecified sentinel — NOT silently to a real
        // currency (e.g. USD). The sentinel is visible to any consumer guard rather than masquerading
        // as a valid currency record.
        var record = new DividendPayment(s_date, 0.10m, Currency.Unspecified, DividendType.Foreign);

        var wire = SerializeToBytes(record);

        DecodeTaggedCurrency(wire, currencyTag: 3).Should().Be(0);
        Roundtrip(wire, DividendPayment.Deserialize).Currency.Should().Be(Currency.Unspecified);
    }

    [Fact]
    public void FxRateDatasetKey_with_Unspecified_currency_surfaces_the_zero_sentinel_explicitly()
    {
        var key = new FxRateDatasetKey("frankfurter", Currency.Unspecified, Currency.USD, DataFrequency.Daily);

        var wire = Wire(key);

        var (decodedBase, decodedQuote) = DecodeKeyCurrencyColumns(wire);
        decodedBase.Should().Be(0, because: "an unspecified base currency encodes the zero sentinel, not a real currency");
        decodedQuote.Should().Be(840);
        FxRateDatasetKey.Deserialize(new MemoryStream(wire)).BaseCurrency.Should().Be(Currency.Unspecified);
    }

    [Fact]
    public void FxPair_rank_logic_treats_Unspecified_as_unranked_without_misbehaving()
    {
        // FxPair.MarketConvention consults a rank dictionary via GetValueOrDefault(.., 0). Unspecified
        // is not in the table, so it ranks 0 (unranked) exactly like any non-G10 currency — the
        // default-shift introduces no new switch arm and no exception.
        var pair = new FxPair(Currency.Unspecified, Currency.USD);

        var convention = pair.MarketConvention();

        // USD (rank 4) outranks the unranked Unspecified, so convention puts USD as the base.
        convention.BaseCurrency.Should().Be(Currency.USD);
        convention.QuoteCurrency.Should().Be(Currency.Unspecified);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private static byte[] SerializeToBytes<TRecord>(TRecord value)
        where TRecord : Boutquin.Storage.Domain.Interfaces.ISerializable<TRecord>
    {
        using var ms = new MemoryStream();
        value.Serialize(ms);
        return ms.ToArray();
    }

    private static byte[] Wire(FxRateDatasetKey key)
    {
        using var ms = new MemoryStream();
        key.Serialize(ms);
        return ms.ToArray();
    }

    private static TRecord Roundtrip<TRecord>(byte[] wire, Func<Stream, TRecord> deserialize)
    {
        using var ms = new MemoryStream(wire);
        return deserialize(ms);
    }

    // Walks a tagged-wire record payload to the currency field and reads its encoded numeric value.
    // The generator frames the record as [ULEB128 payload-length | payload]; the payload is a tag-0
    // schema-version varint followed by the tagged fields. An Int32-underlying enum field is written
    // as a zigzag varint, so the decoded value is the currency's ISO 4217 number.
    private static long DecodeTaggedCurrency(byte[] framed, int currencyTag)
    {
        using var ms = new MemoryStream(framed);
        var payloadLength = (int)VarintHelpers.ReadVarUInt64(ms);
        var payloadEnd = ms.Position + payloadLength;

        while (ms.Position < payloadEnd && VarintHelpers.TryReadTag(ms, out var tag, out var wireType))
        {
            if (tag == currencyTag)
            {
                wireType.Should().Be(WireType.Varint, "an Int32-underlying enum is written on the Varint wire");
                return VarintHelpers.ReadZigZagInt64(ms);
            }

            VarintHelpers.SkipField(ms, wireType);
        }

        throw new InvalidOperationException($"Currency tag {currencyTag} was not present in the payload.");
    }

    // Decodes the two currency columns of an FxRateDatasetKey ordered wire. Layout (declaration
    // order): [Provider string][BaseCurrency int32][QuoteCurrency int32][Frequency int32]. Each
    // trailing column is a fixed 4-byte big-endian (OrderedByteHelpers.WriteInt32), so the last 12
    // bytes are exactly base|quote|frequency regardless of the variable-length provider prefix.
    private static (int BaseIso, int QuoteIso) DecodeKeyCurrencyColumns(byte[] wire)
    {
        wire.Length.Should().BeGreaterThanOrEqualTo(12, "the three trailing int32 columns occupy 12 bytes");

        using var baseStream = new MemoryStream(wire[(wire.Length - 12)..(wire.Length - 8)]);
        using var quoteStream = new MemoryStream(wire[(wire.Length - 8)..(wire.Length - 4)]);
        return (OrderedByteHelpers.ReadInt32(baseStream), OrderedByteHelpers.ReadInt32(quoteStream));
    }
}
