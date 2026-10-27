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

using System.Text.Json;

using Boutquin.MarketData.Abstractions.Contracts;

namespace Boutquin.MarketData.Storage.Serializers;

/// <summary>
/// JSON-based snapshot serializer using <see cref="JsonSerializer"/>. Works with any
/// <see cref="JsonSerializer"/>-compatible type, providing a zero-configuration default
/// for <see cref="ISnapshotSerializer{T}"/> registrations.
/// </summary>
/// <typeparam name="T">The type to serialize. Must be compatible with System.Text.Json.</typeparam>
/// <remarks>
/// Uses camelCase property naming and compact (non-indented) output. The serializer is
/// stateless and thread-safe. Deserialization throws <see cref="InvalidOperationException"/>
/// if the stream contains a JSON <c>null</c> literal. Null payloads are rejected before
/// serialization writes any bytes, including empty nullable value types.
/// </remarks>
public sealed class JsonSnapshotSerializer<T> : ISnapshotSerializer<T>
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Null payloads cannot round-trip through this serializer and are rejected before changing
    /// <paramref name="stream"/>. The caller retains ownership of the stream.
    /// </remarks>
    public async Task SerializeAsync(Stream stream, T data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        await JsonSerializer.SerializeAsync(stream, data, s_options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<T> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return await JsonSerializer.DeserializeAsync<T>(stream, s_options, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Deserialized snapshot was null.");
    }
}
