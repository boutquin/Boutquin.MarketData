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

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.Storage.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit.Snapshots;

public sealed class DataProvenancePriceBasisSerializationTests
{
    [Fact]
    public async Task Metadata_round_trips_with_and_without_price_basis()
    {
        foreach (var basis in new PriceBasis?[] { new(PriceAdjustment.All, AdjustedCloseSource.AdapterComputed, PriceAdjustment.Splits), null })
        {
            var provenance = new DataProvenance(new ProviderCode("fixture"), "SPY", LicenseType.Unknown, RetrievalMode.Live, FreshnessClass.EndOfDay, DateTimeOffset.UnixEpoch, null) { PriceBasis = basis };
            var serializer = new SnapshotEnvelopeSerializer<string>(new StringSerializer());
            using var stream = new MemoryStream();
            await serializer.SerializeAsync(stream, new(new SchemaVersion(1), new(DateTimeOffset.UnixEpoch, [provenance], []), "payload"));
            stream.Position = 0;
            var restored = await serializer.DeserializeAsync(stream);
            restored.Metadata.Provenance.Should().Equal(provenance);
            restored.Metadata.Provenance[0].PriceBasis.Should().Be(basis);
        }
    }

    [Fact]
    public async Task Legacy_metadata_without_price_basis_reads_null()
    {
        var provenance = new DataProvenance(new ProviderCode("fixture"), "SPY", LicenseType.Unknown, RetrievalMode.Live, FreshnessClass.EndOfDay, DateTimeOffset.UnixEpoch, null);
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new SnapshotMetadata(DateTimeOffset.UnixEpoch, [provenance], [])))!;
        node["Provenance"]![0]!.AsObject().Remove("PriceBasis");
        var bytes = Encoding.UTF8.GetBytes(node.ToJsonString());
        using var stream = new MemoryStream();
        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), bytes.Length);
        stream.Write(header);
        stream.Write(bytes);
        await new StringSerializer().SerializeAsync(stream, "legacy");
        stream.Position = 0;
        var restored = await new SnapshotEnvelopeSerializer<string>(new StringSerializer()).DeserializeAsync(stream);
        restored.Metadata.Provenance[0].PriceBasis.Should().BeNull();
        restored.Payload.Should().Be("legacy");
    }

    private sealed class StringSerializer : ISnapshotSerializer<string>
    {
        public async Task SerializeAsync(Stream stream, string data, CancellationToken cancellationToken = default) =>
            await stream.WriteAsync(Encoding.UTF8.GetBytes(data), cancellationToken);
        public async Task<string> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken);
        }
    }
}
