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
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;

namespace Boutquin.MarketData.Orchestration.Snapshots;

/// <summary>
/// Per-record-type binding that lets <see cref="DataSnapshot"/> capture and read through a
/// point-in-time engine snapshot without itself knowing the record's <c>TDatasetKey</c>. One
/// implementation is registered per record type (resolved from DI by the snapshot path); it owns
/// the <c>(TDatasetKey, engine, dataset-key build, range read, gap detection)</c> machinery that
/// is generic over <c>TDatasetKey</c>.
/// </summary>
/// <typeparam name="TRecord">The dated, columnar-rehydratable record type.</typeparam>
/// <remarks>
/// The binding hides <c>TDatasetKey</c> behind <typeparamref name="TRecord"/> so the
/// <c>IDataSnapshot.ExecuteAsync</c> surface can be keyed on the record type alone — the dataset
/// key is reconstructed inside the binding from the request, exactly as the live pipeline does.
/// The binding never dispatches an adapter — a gap is reported back to
/// <see cref="DataSnapshot"/>, which raises <c>SnapshotGapException</c>.
/// </remarks>
public interface ISnapshotReadBinding<TRecord>
    where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
{
    /// <summary>
    /// Resolves the engine for <typeparamref name="TRecord"/> via
    /// <see cref="ITimeSeriesStoreRegistry.ResolveEngine{TDatasetKey, TRecord}"/> and acquires a
    /// fresh point-in-time snapshot. Called once per record type; the returned handle is cached
    /// and reused by <see cref="DataSnapshot"/> for the rest of its lifetime.
    /// </summary>
    /// <param name="registry">The store registry exposing <c>ResolveEngine</c>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The captured engine snapshot, boxed as <see cref="IAsyncDisposable"/>.</returns>
    Task<IAsyncDisposable> CaptureAsync(ITimeSeriesStoreRegistry registry, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the request's range through a previously captured snapshot, returning the visible
    /// records and whether the range had a gap. Performs NO adapter dispatch.
    /// </summary>
    /// <param name="snapshot">The handle returned by <see cref="CaptureAsync"/>.</param>
    /// <param name="request">The data request describing the dataset and date range.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The visible records plus a gap flag.</returns>
    Task<SnapshotReadResult<TRecord>> ReadAsync(
        IAsyncDisposable snapshot, IDataRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Result of a snapshot-bound range read: the records visible under the snapshot, plus whether
/// the requested range had an uncovered gap.
/// </summary>
/// <typeparam name="TRecord">The dated record type.</typeparam>
/// <param name="Records">The records visible under the snapshot for the requested range.</param>
/// <param name="HasGap">
/// <see langword="true"/> when the requested range is not fully covered by the snapshot; the
/// snapshot path translates this into a <c>SnapshotGapException</c>.
/// </param>
public readonly record struct SnapshotReadResult<TRecord>(
    IReadOnlyList<TRecord> Records,
    bool HasGap)
{
    /// <summary>A defensive snapshot of the supplied records, exposed as a read-only collection.</summary>
    public IReadOnlyList<TRecord> Records
    {
        get;
        init => field = CopyRecords(value);
    } = CopyRecords(Records);

    private static IReadOnlyList<TRecord> CopyRecords(IReadOnlyList<TRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return Array.AsReadOnly(records.ToArray());
    }
}
