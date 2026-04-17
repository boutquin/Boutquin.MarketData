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

using Boutquin.Storage.Domain.ValueObjects;

namespace Boutquin.MarketData.Storage.Snapshots;

/// <summary>
/// Wraps a data payload with a schema version and metadata (timestamp + provenance)
/// for durable snapshot storage. The envelope travels as a single serialized unit so
/// that version, metadata, and payload are always co-located.
/// </summary>
/// <typeparam name="T">The type of the data payload.</typeparam>
/// <param name="Version">The schema version that the payload conforms to.
/// Used by readers to dispatch compatible deserialization when the payload schema evolves.</param>
/// <param name="Metadata">Storage timestamp and provenance metadata describing the fetch that produced this snapshot.</param>
/// <param name="Payload">The data payload itself.</param>
/// <remarks>
/// <para>
/// Version is a first-class envelope field (not buried inside the payload or metadata) so
/// that migration tools and schema compatibility checks can inspect it without deserializing
/// the payload. This mirrors <see cref="EventEnvelope{TKey, TValue}"/> in Boutquin.Storage.
/// </para>
/// </remarks>
public sealed record SnapshotEnvelope<T>(
    SchemaVersion Version,
    SnapshotMetadata Metadata,
    T Payload);
