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
using Boutquin.Storage.Domain.Interfaces.Events;

namespace Boutquin.MarketData.Storage.Events;

/// <summary>
/// Typed wrapper around <see cref="IEventLog{TEvent}"/> for ingestion audit trail events.
/// Provides a MarketData-specific API over the generic append-only event log.
/// </summary>
/// <remarks>
/// Events are appended with monotonically increasing positions, enabling
/// replay from any point in the ingestion history. Use
/// <see cref="ReadFromAsync"/> with position 0 to replay the full log.
/// </remarks>
public sealed class IngestionEventLog
{
    private readonly IEventLog<IngestionEvent> _inner;

    /// <summary>
    /// Initializes a new <see cref="IngestionEventLog"/>.
    /// </summary>
    /// <param name="inner">The backing event log.</param>
    public IngestionEventLog(IEventLog<IngestionEvent> inner) => _inner = inner;

    /// <summary>
    /// Appends an ingestion event to the log.
    /// </summary>
    /// <param name="evt">The event to log.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The monotonically increasing position assigned to the event.</returns>
    public Task<long> LogAsync(IngestionEvent evt, CancellationToken cancellationToken = default)
        => _inner.AppendAsync(evt, cancellationToken).AsTask();

    /// <summary>
    /// Reads events from the specified position forward.
    /// </summary>
    /// <param name="position">The position to start reading from (inclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of (position, event) tuples.</returns>
    public IAsyncEnumerable<(long Position, IngestionEvent Event)> ReadFromAsync(
        long position, CancellationToken cancellationToken = default)
        => _inner.ReadFromAsync(position, cancellationToken);
}
