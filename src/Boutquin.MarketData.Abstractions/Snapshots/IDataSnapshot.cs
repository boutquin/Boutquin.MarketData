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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;

namespace Boutquin.MarketData.Abstractions.Snapshots;

/// <summary>
/// Read handle whose first read for each record type captures that type's engine watermark.
/// Acquired via <c>IDataPipeline.AcquireReadSnapshotAsync</c>; creating the handle does not
/// capture engine state. Subsequent reads of the same type reuse its captured view.
/// This is not a cross-record-type atomic snapshot. Disposal releases all captured snapshots.
/// </summary>
/// <remarks>
/// <para><b>Isolation.</b> Reads reflect the watermark captured by the first read of their
/// record type, not handle creation. Entries written after that capture are invisible to the
/// type's later reads. Different record types may capture different watermarks.</para>
/// <para><b>Supported types.</b> Only columnar-rehydratable types with a registered snapshot
/// binding are supported. Row-only <c>DividendPayment</c> and <c>FuturesSettlement</c> cannot be
/// read through this handle. A row-format store for a columnar-rehydratable type is supported.</para>
/// <para><b>Gap behaviour.</b> Unlike the live pipeline, a snapshot read NEVER dispatches an
/// adapter to fill a gap. A requested range that the snapshot cannot fully cover throws a
/// <see cref="SnapshotGapException"/> — the handle's whole purpose is stability, and fetching a
/// gap would both violate that guarantee and leave the newly appended records invisible to the
/// same snapshot. Callers who want "start from a snapshot, fall back to live" compose a snapshot
/// read and a live <c>ExecuteAsync</c> explicitly.</para>
/// <para><b>L1 bypass.</b> The in-memory TTL cache is bypassed entirely inside the handle — its
/// hot-path semantics would violate snapshot isolation.</para>
/// <para><b>Lifecycle &amp; concurrency.</b> The handle is NOT thread-safe — acquire one per
/// execution context (one backtest / calibration run) and issue reads sequentially. Snapshot
/// capture is once-per-record-type and reused for the handle's whole lifetime. After
/// <see cref="IAsyncDisposable.DisposeAsync"/>, <see cref="ExecuteAsync{TRecord}"/> throws
/// <see cref="ObjectDisposedException"/>; double-dispose is a no-op. Consumers MUST use the
/// <c>await using</c> pattern — a GC-finalised handle leaves its segment pins in place across
/// compaction until the finaliser runs.</para>
/// </remarks>
public interface IDataSnapshot : IAsyncDisposable
{
    /// <summary>
    /// Reads the requested range for <typeparamref name="TRecord"/> through this handle's
    /// point-in-time view. The first call for a record type captures the engine snapshot;
    /// subsequent calls reuse it.
    /// </summary>
    /// <typeparam name="TRecord">The dated, columnar-rehydratable record type.</typeparam>
    /// <param name="request">The data request describing the dataset and date range.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An envelope wrapping the records visible under the snapshot for the request's range.</returns>
    /// <exception cref="SnapshotGapException">
    /// The requested range is not fully covered by the snapshot. The snapshot path fails fast
    /// rather than dispatching an adapter.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The handle has been disposed.</exception>
    Task<DataEnvelope<IReadOnlyList<TRecord>>> ExecuteAsync<TRecord>(
        IDataRequest request,
        CancellationToken cancellationToken = default)
        where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new();
}
