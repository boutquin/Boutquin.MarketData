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
using Boutquin.Storage.Domain.Interfaces.Cdc;
using Boutquin.Storage.Domain.ValueObjects;

namespace Boutquin.MarketData.Storage.Cdc;

/// <summary>
/// Typed wrapper around <see cref="IChangeDataCaptureSource{TKey, TValue}"/>
/// for snapshot change tracking, enabling downstream consumers to react to
/// snapshot store mutations.
/// </summary>
/// <remarks>
/// Key type is <c>string</c> (snapshot key), value type is <c>byte[]</c> (raw content).
/// Back this with <see cref="Boutquin.Storage.Infrastructure.Cdc.EventLogCdcSource{TKey, TValue}"/>
/// to derive CDC from an event log.
/// </remarks>
public sealed class SnapshotChangeSource
{
    private readonly IChangeDataCaptureSource<string, byte[]> _inner;

    /// <summary>
    /// Initializes a new <see cref="SnapshotChangeSource"/>.
    /// </summary>
    /// <param name="inner">The backing CDC source.</param>
    public SnapshotChangeSource(IChangeDataCaptureSource<string, byte[]> inner)
        => _inner = inner;

    /// <summary>
    /// Reads change records from the specified position forward.
    /// </summary>
    /// <param name="position">The position to start reading from (inclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of change records.</returns>
    public IAsyncEnumerable<ChangeRecord<string, byte[]>> ReadChangesAsync(
        long position, CancellationToken cancellationToken = default)
        => _inner.ReadChangesAsync(position, cancellationToken);
}
