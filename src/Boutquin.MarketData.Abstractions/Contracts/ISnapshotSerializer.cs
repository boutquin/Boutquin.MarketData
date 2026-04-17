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
namespace Boutquin.MarketData.Abstractions.Contracts;

/// <summary>
/// Handles domain-specific serialization and deserialization of data to and from streams,
/// pairing with <see cref="ISnapshotStore"/> which handles raw stream persistence.
/// </summary>
/// <typeparam name="T">The type of data to serialize.</typeparam>
/// <remarks>
/// <para>
/// The kernel separates storage (<see cref="ISnapshotStore"/>) from serialization
/// (<see cref="ISnapshotSerializer{T}"/>) so that domain repos can define their own
/// wire formats (CSV, JSON, Parquet) without the kernel knowing the format.
/// </para>
/// <para>
/// Implementations must be stateless and thread-safe. The serializer does not own
/// the stream — callers are responsible for stream lifecycle.
/// </para>
/// </remarks>
public interface ISnapshotSerializer<T>
{
    /// <summary>
    /// Serializes data to the provided stream.
    /// </summary>
    /// <param name="stream">The stream to write to. Must be writable. Position is not reset before writing.</param>
    /// <param name="data">The data to serialize.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SerializeAsync(Stream stream, T data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deserializes data from the provided stream.
    /// </summary>
    /// <param name="stream">The stream to read from. Must be readable. Reading starts at current position.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The deserialized data.</returns>
    Task<T> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default);
}
