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
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.Storage.Domain.ValueObjects;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit.Snapshots;

public sealed class SnapshotEnvelopeSerializerTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Issues\":[]}")]
    [InlineData("{\"Provenance\":[]}")]
    [InlineData("{\"Provenance\":null,\"Issues\":[]}")]
    [InlineData("{\"Provenance\":[],\"Issues\":null}")]
    public async Task Deserialize_missing_required_collections_rejects_before_payload(string metadataJson)
    {
        var serializer = new SnapshotEnvelopeSerializer<string>(new StringSerializer());
        var metadataBytes = Encoding.UTF8.GetBytes(metadataJson);
        using var stream = new MemoryStream();
        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), metadataBytes.Length);
        stream.Write(header);
        stream.Write(metadataBytes);
        var payloadPosition = stream.Position;
        stream.Write(Encoding.UTF8.GetBytes("payload"));
        stream.Position = 0;

        var read = () => serializer.DeserializeAsync(stream);

        await read.Should().ThrowAsync<InvalidDataException>();
        stream.Position.Should().Be(payloadPosition);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Serialize_missing_required_collections_rejects_before_writing(bool missingProvenance)
    {
        var metadata = new SnapshotMetadata(DateTimeOffset.UnixEpoch,
            missingProvenance ? null! : [], missingProvenance ? [] : null!);
        var envelope = new SnapshotEnvelope<string>(new SchemaVersion(1), metadata, "payload");
        var serializer = new SnapshotEnvelopeSerializer<string>(new StringSerializer());
        using var stream = new MemoryStream();

        var write = () => serializer.SerializeAsync(stream, envelope);

        await write.Should().ThrowAsync<InvalidDataException>();
        stream.Length.Should().Be(0);
    }

    [Fact]
    public async Task Serialize_null_metadata_rejects_before_writing()
    {
        var serializer = new SnapshotEnvelopeSerializer<string>(new StringSerializer());
        var envelope = new SnapshotEnvelope<string>(new SchemaVersion(1), null!, "payload");
        using var stream = new MemoryStream();
        stream.WriteByte(42);
        var before = stream.ToArray();
        var position = stream.Position;

        var write = () => serializer.SerializeAsync(stream, envelope);

        await write.Should().ThrowAsync<ArgumentException>();
        stream.ToArray().Should().Equal(before);
        stream.Position.Should().Be(position);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Metadata_byte_limit_is_symmetric_and_rejection_writes_nothing(int excess)
    {
        const int limit = 1024 * 1024;
        var empty = new SnapshotMetadata(DateTimeOffset.UnixEpoch, [],
            [new DataIssue(IssueCode.MissingField, IssueSeverity.Warning, string.Empty)]);
        var overhead = JsonSerializer.SerializeToUtf8Bytes(empty).Length;
        var metadata = empty with
        {
            Issues = [new DataIssue(IssueCode.MissingField, IssueSeverity.Warning, new string('x', limit - overhead + excess))]
        };
        var serializer = new SnapshotEnvelopeSerializer<string>(new StringSerializer());
        var envelope = new SnapshotEnvelope<string>(new SchemaVersion(1), metadata, "payload");
        using var stream = new MemoryStream();

        if (excess > 0)
        {
            var write = () => serializer.SerializeAsync(stream, envelope);
            await write.Should().ThrowAsync<InvalidDataException>("writer and reader must agree on the metadata byte limit");
            stream.Length.Should().Be(0, "validation must precede even the version header");
        }
        else
        {
            await serializer.SerializeAsync(stream, envelope);
            stream.Position = 0;
            (await serializer.DeserializeAsync(stream)).Metadata.Issues[0].Message.Should().Be(metadata.Issues[0].Message);
        }
    }

    [Fact]
    public async Task RoundTrip_preserves_envelope()
    {
        // Arrange
        var storedAt = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var provenance = new List<DataProvenance>
        {
            new(new ProviderCode("tiingo"), "AAPL", LicenseType.Free, RetrievalMode.Api, FreshnessClass.EndOfDay, storedAt, null)
        };
        var metadata = new SnapshotMetadata(storedAt, provenance, []);
        var version = new SchemaVersion(7);
        var envelope = new SnapshotEnvelope<string>(version, metadata, "test-payload");
        var serializer = new SnapshotEnvelopeSerializer<string>(new StringSerializer());

        using var stream = new MemoryStream();

        // Act
        await serializer.SerializeAsync(stream, envelope).ConfigureAwait(true);
        stream.Position = 0;
        var result = await serializer.DeserializeAsync(stream).ConfigureAwait(true);

        // Assert
        result.Version.Should().Be(version);
        result.Payload.Should().Be("test-payload");
        result.Metadata.StoredAtUtc.Should().Be(storedAt);
        result.Metadata.Provenance.Should().HaveCount(1);
        result.Metadata.Provenance[0].Provider.Should().Be(new ProviderCode("tiingo"));
    }

    [Fact]
    public async Task Metadata_StoredAtUtc_and_Provenance_survive_roundtrip()
    {
        // Arrange
        var storedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var prov1 = new DataProvenance(new ProviderCode("fred"), "DGS10", LicenseType.Free, RetrievalMode.Api, FreshnessClass.Live, storedAt, "https://example.com");
        var prov2 = new DataProvenance(new ProviderCode("boc"), "CORRA", LicenseType.Free, RetrievalMode.Snapshot, FreshnessClass.Delayed, storedAt, null);
        var metadata = new SnapshotMetadata(storedAt, [prov1, prov2], []);
        var envelope = new SnapshotEnvelope<string>(new SchemaVersion(1), metadata, "data");
        var serializer = new SnapshotEnvelopeSerializer<string>(new StringSerializer());

        using var stream = new MemoryStream();

        // Act
        await serializer.SerializeAsync(stream, envelope).ConfigureAwait(true);
        stream.Position = 0;
        var result = await serializer.DeserializeAsync(stream).ConfigureAwait(true);

        // Assert
        result.Metadata.StoredAtUtc.Should().Be(storedAt);
        result.Metadata.Provenance.Should().HaveCount(2);
        result.Metadata.Provenance[0].Provider.Should().Be(new ProviderCode("fred"));
        result.Metadata.Provenance[1].Provider.Should().Be(new ProviderCode("boc"));
    }

    [Fact]
    public async Task Metadata_Coverage_survives_roundtrip()
    {
        // Arrange
        var coverage = new DataCoverage(2, 1, 1, 0.5m);
        var metadata = new SnapshotMetadata(DateTimeOffset.UtcNow, [], [])
        {
            Coverage = coverage
        };
        var envelope = new SnapshotEnvelope<string>(new SchemaVersion(1), metadata, "data");
        var serializer = new SnapshotEnvelopeSerializer<string>(new StringSerializer());

        using var stream = new MemoryStream();

        // Act
        await serializer.SerializeAsync(stream, envelope).ConfigureAwait(true);
        stream.Position = 0;
        var result = await serializer.DeserializeAsync(stream).ConfigureAwait(true);

        // Assert
        result.Metadata.Coverage.Should().Be(coverage);
    }

    [Fact]
    public async Task Metadata_without_Coverage_field_remains_readable()
    {
        // Arrange: reproduce the metadata JSON written before Coverage was additive.
        const string legacyMetadataJson =
            "{\"StoredAtUtc\":\"2025-01-01T00:00:00+00:00\",\"Provenance\":[],\"Issues\":[]}";
        var metadataBytes = Encoding.UTF8.GetBytes(legacyMetadataJson);
        var serializer = new SnapshotEnvelopeSerializer<string>(new StringSerializer());

        using var stream = new MemoryStream();
        var versionBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(versionBytes, 1);
        await stream.WriteAsync(versionBytes).ConfigureAwait(true);
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, metadataBytes.Length);
        await stream.WriteAsync(lengthBytes).ConfigureAwait(true);
        await stream.WriteAsync(metadataBytes).ConfigureAwait(true);
        await stream.WriteAsync(Encoding.UTF8.GetBytes("legacy-payload")).ConfigureAwait(true);
        stream.Position = 0;

        // Act
        var result = await serializer.DeserializeAsync(stream).ConfigureAwait(true);

        // Assert
        result.Metadata.Coverage.Should().BeNull();
        result.Payload.Should().Be("legacy-payload");
    }

    [Fact]
    public async Task Inner_serializer_receives_correct_payload_bytes()
    {
        // Arrange
        var innerSerializer = new StringSerializer();
        var serializer = new SnapshotEnvelopeSerializer<string>(innerSerializer);
        var metadata = new SnapshotMetadata(DateTimeOffset.UtcNow, [], []);
        var envelope = new SnapshotEnvelope<string>(new SchemaVersion(1), metadata, "hello-world");

        using var stream = new MemoryStream();

        // Act
        await serializer.SerializeAsync(stream, envelope).ConfigureAwait(true);
        stream.Position = 0;
        var result = await serializer.DeserializeAsync(stream).ConfigureAwait(true);

        // Assert
        result.Payload.Should().Be("hello-world");
    }

    [Fact]
    public async Task Empty_provenance_list_is_valid()
    {
        // Arrange
        var metadata = new SnapshotMetadata(DateTimeOffset.UtcNow, [], []);
        var envelope = new SnapshotEnvelope<string>(new SchemaVersion(1), metadata, "x");
        var serializer = new SnapshotEnvelopeSerializer<string>(new StringSerializer());

        using var stream = new MemoryStream();

        // Act
        await serializer.SerializeAsync(stream, envelope).ConfigureAwait(true);
        stream.Position = 0;
        var result = await serializer.DeserializeAsync(stream).ConfigureAwait(true);

        // Assert
        result.Metadata.Provenance.Should().BeEmpty();
        result.Payload.Should().Be("x");
    }

    /// <summary>
    /// Simple test double that serializes strings as UTF-8 bytes.
    /// </summary>
    private sealed class StringSerializer : ISnapshotSerializer<string>
    {
        public async Task SerializeAsync(Stream stream, string data, CancellationToken cancellationToken = default)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(true);
        }

        public async Task<string> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(true);
        }
    }
}
