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
using System.Text.Json;

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.Storage.Domain.ValueObjects;

namespace Boutquin.MarketData.Storage.Snapshots;

/// <summary>
/// Wraps an <see cref="ISnapshotSerializer{T}"/> to produce a <see cref="SnapshotEnvelope{T}"/>
/// carrying schema version, timestamp, and provenance alongside the payload on disk.
/// </summary>
/// <typeparam name="T">The payload type delegated to the inner serializer.</typeparam>
/// <remarks>
/// <para>
/// Wire format (all little-endian):
/// <code>
/// [4-byte SchemaVersion.Value]
/// [4-byte metadata JSON length]
/// [metadata JSON bytes]
/// [inner-serialized payload bytes]
/// </code>
/// </para>
/// <para>
/// The version header is written as a fixed-width binary prefix so that readers can inspect
/// the schema version without decoding metadata JSON or the payload. The metadata header is
/// JSON; the payload format is determined by the inner serializer.
/// </para>
/// </remarks>
public sealed class SnapshotEnvelopeSerializer<T> : ISnapshotSerializer<SnapshotEnvelope<T>>
{
    /// <summary>
    /// Upper bound on the metadata-header length. Chosen so that legitimate metadata (a few KiB
    /// of provenance + issues) stays well under the limit while a corrupted or malicious header
    /// prefix cannot induce a gigabyte-scale allocation.
    /// </summary>
    private const int MaxMetaJsonBytes = 1 * 1024 * 1024;

    private readonly ISnapshotSerializer<T> _innerSerializer;

    /// <summary>
    /// Initializes a new <see cref="SnapshotEnvelopeSerializer{T}"/>.
    /// </summary>
    /// <param name="innerSerializer">The serializer for the payload portion.</param>
    public SnapshotEnvelopeSerializer(ISnapshotSerializer<T> innerSerializer)
    {
        _innerSerializer = innerSerializer ?? throw new ArgumentNullException(nameof(innerSerializer));
    }

    /// <inheritdoc />
    public async Task SerializeAsync(Stream stream, SnapshotEnvelope<T> data, CancellationToken cancellationToken = default)
    {
        var versionBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(versionBytes, data.Version.Value);
        await stream.WriteAsync(versionBytes, cancellationToken).ConfigureAwait(false);

        var metaJson = JsonSerializer.SerializeToUtf8Bytes(data.Metadata);
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, metaJson.Length);
        await stream.WriteAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(metaJson, cancellationToken).ConfigureAwait(false);

        await _innerSerializer.SerializeAsync(stream, data.Payload, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SnapshotEnvelope<T>> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var versionBytes = new byte[4];
        await stream.ReadExactlyAsync(versionBytes, cancellationToken).ConfigureAwait(false);
        var version = new SchemaVersion(BinaryPrimitives.ReadInt32LittleEndian(versionBytes));

        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        var metaLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);

        if (metaLength < 0 || metaLength > MaxMetaJsonBytes)
        {
            throw new InvalidDataException(
                $"Snapshot metadata length {metaLength} is outside the accepted range [0, {MaxMetaJsonBytes}]. " +
                "Possible causes: corrupted header, truncated file, or hostile input.");
        }

        var metaJson = new byte[metaLength];
        await stream.ReadExactlyAsync(metaJson, cancellationToken).ConfigureAwait(false);
        var metadata = JsonSerializer.Deserialize<SnapshotMetadata>(metaJson)
            ?? throw new InvalidDataException("Snapshot metadata deserialized to null.");

        var payload = await _innerSerializer.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
        return new SnapshotEnvelope<T>(version, metadata, payload);
    }
}
