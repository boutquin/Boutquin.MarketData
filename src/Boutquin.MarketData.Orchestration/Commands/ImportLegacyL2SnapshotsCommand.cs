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

using System.Collections;

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;

namespace Boutquin.MarketData.Orchestration.Commands;

/// <summary>
/// A single legacy L2 snapshot to warm-start into the time-series store: the original typed
/// <paramref name="Request"/> that produced it (so the importer can project its dataset key via the
/// registered <see cref="IDatasetKeyBuilder{TRequest, TDatasetKey}"/>) plus the canonical
/// <paramref name="Records"/> parsed from the snapshot payload.
/// </summary>
/// <typeparam name="TRequest">The concrete request type the legacy snapshot was keyed by.</typeparam>
/// <typeparam name="TRecord">The dated canonical record type stored in the snapshot payload.</typeparam>
/// <param name="Request">
/// The request whose identity fields (symbol, frequency, venue, series id, …) the importer projects
/// into the target <c>TDatasetKey</c>. This is the same request an <c>ExecuteAsync</c> caller would
/// have issued, so the resulting key matches what the live pipeline reads and writes.
/// </param>
/// <param name="Records">
/// The canonical records parsed from the legacy L2 snapshot payload (e.g. a
/// <c>SnapshotEnvelope&lt;IReadOnlyList&lt;Bar&gt;&gt;.Payload</c>). Appended verbatim; the importer
/// performs no re-normalization — warm-start is a pure copy of already-canonical data.
/// </param>
public readonly record struct LegacyL2Snapshot<TRequest, TRecord>(
    TRequest Request,
    IReadOnlyList<TRecord> Records)
    where TRequest : IDataRequest
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

/// <summary>
/// Source of legacy L2 snapshots for the warm-start importer. A site that adopted the time-series
/// store implements this to enumerate its existing L2 snapshots as
/// <see cref="LegacyL2Snapshot{TRequest, TRecord}"/> pairs.
/// </summary>
/// <typeparam name="TRequest">The concrete request type the legacy snapshots were keyed by.</typeparam>
/// <typeparam name="TRecord">The dated canonical record type stored in the snapshot payloads.</typeparam>
/// <remarks>
/// <para>
/// The kernel's <see cref="ISnapshotStore"/> is a key→stream store with no enumeration surface, and a
/// legacy L2 payload is an opaque serialized <c>SnapshotEnvelope</c> keyed by an opaque pipeline cache
/// key. The set of keys a site holds is site-specific (it derives from the requests that site has
/// issued), so the importer cannot discover them — the adopting site supplies them by reading each
/// known cache key back through its <c>WriteThroughCache</c> and yielding the parsed payload here.
/// </para>
/// <para>
/// This keeps the importer strictly opt-in: nothing is imported unless the site enumerates its
/// own snapshots, and the site — which knows its request identities — owns the payload→record parse.
/// </para>
/// </remarks>
public interface ILegacyL2SnapshotSource<TRequest, TRecord>
    where TRequest : IDataRequest
{
    /// <summary>
    /// Enumerates the site's existing legacy L2 snapshots as <c>(request, records)</c> pairs.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    IAsyncEnumerable<LegacyL2Snapshot<TRequest, TRecord>> EnumerateAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The result of a warm-start import: how many snapshots were read and how many
/// <see cref="ITimeSeriesStore{TDatasetKey, TRecord}.AppendAsync"/> calls the importer issued
/// (one per enumerated snapshot).
/// </summary>
/// <param name="SnapshotsRead">Total legacy snapshots enumerated from the source.</param>
/// <param name="AppendCallCount">
/// Number of <c>AppendAsync</c> calls issued into the time-series store. Empty snapshots are
/// appended as a no-op, so this count equals <paramref name="SnapshotsRead"/>.
/// </param>
public readonly record struct ImportLegacyL2SnapshotsResult(
    int SnapshotsRead,
    int AppendCallCount);

/// <summary>
/// Optional one-shot warm-start command: reads existing legacy L2 snapshots, resolves the
/// matching time-series store + dataset key for each, and <c>AppendAsync</c>es the parsed records —
/// so a site that adopts the store avoids the first-request-per-key adapter fetch by pre-populating
/// the store from data it already holds. Strictly opt-in: nothing runs unless a site constructs the
/// command with its own <see cref="ILegacyL2SnapshotSource{TRequest, TRecord}"/> and invokes it.
/// </summary>
/// <typeparam name="TRequest">The concrete request type the legacy snapshots were keyed by.</typeparam>
/// <typeparam name="TRecord">The dated canonical record type stored in the snapshot payloads.</typeparam>
/// <typeparam name="TDatasetKey">
/// The dataset partition key produced from <typeparamref name="TRequest"/> by the registered
/// <see cref="IDatasetKeyBuilder{TRequest, TDatasetKey}"/>; the same key the live pipeline reads and
/// writes for this record type.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>Idempotent on second run.</b> The command adds no dedup logic of its own.
/// Idempotency is inherited from the store: appends are keyed by the composite
/// <c>(DatasetKey, Date)</c>, resolved last-writer-wins in memtable/seqno order, so re-appending the
/// same snapshot records collapses to the same slots and <c>GetRangeAsync</c> returns the identical
/// set on the second run — no doubling (spec "AppendAsync read-side idempotency").
/// </para>
/// <para>
/// <b>Explicit opt-in per record type.</b> If no time-series store is registered for
/// <typeparamref name="TRecord"/> (the record type is not backed by the store, or the kernel was not
/// configured with it), <see cref="ExecuteAsync"/> throws <see cref="NotSupportedException"/> naming
/// the unmapped record type rather than silently dropping the snapshot. Warm-start is deliberate: an
/// unmapped type is a configuration error the operator must see, not a no-op.
/// </para>
/// </remarks>
public sealed class ImportLegacyL2SnapshotsCommand<TRequest, TRecord, TDatasetKey>
    where TRequest : IDataRequest
    where TRecord : ISerializable<TRecord>, IHasDate, new()
    where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
{
    private readonly ITimeSeriesStoreRegistry _registry;
    private readonly IDatasetKeyBuilder<TRequest, TDatasetKey> _keyBuilder;
    private readonly ILegacyL2SnapshotSource<TRequest, TRecord> _source;

    /// <summary>
    /// Creates the warm-start importer.
    /// </summary>
    /// <param name="registry">
    /// The live registry the pipeline uses; the importer resolves the same store instance the
    /// production read/write path uses, so the imported records are visible to subsequent
    /// <c>ExecuteAsync</c> reads.
    /// </param>
    /// <param name="keyBuilder">
    /// The registered request→dataset-key projection for this record type — the same builder the
    /// pipeline resolves, so the imported records land under the key a live request would read.
    /// </param>
    /// <param name="source">The adopting site's enumeration of its existing legacy L2 snapshots.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public ImportLegacyL2SnapshotsCommand(
        ITimeSeriesStoreRegistry registry,
        IDatasetKeyBuilder<TRequest, TDatasetKey> keyBuilder,
        ILegacyL2SnapshotSource<TRequest, TRecord> source)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _keyBuilder = keyBuilder ?? throw new ArgumentNullException(nameof(keyBuilder));
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>
    /// Runs the warm-start import: for each enumerated legacy snapshot, projects the dataset key and
    /// appends the snapshot's records into the resolved time-series store.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>
    /// A <see cref="ImportLegacyL2SnapshotsResult"/> with the snapshot count and the number of
    /// <c>AppendAsync</c> calls issued (one per snapshot, so <c>AppendCallCount == SnapshotsRead</c>).
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// No time-series store is registered for <typeparamref name="TRecord"/> — the record type is not
    /// warm-startable under the current kernel configuration. The message names the record type.
    /// Also thrown for factor, yield-curve, futures, or option-chain requests, whose per-row
    /// identities require a fan-out projection that this single-key importer does not perform.
    /// </exception>
    public async Task<ImportLegacyL2SnapshotsResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var store = _registry.Resolve<TDatasetKey, TRecord>()
            ?? throw new NotSupportedException(
                $"No time-series store is registered for record type '{typeof(TRecord).Name}'; the "
                + "warm-start importer cannot map its snapshots. Register a store for this record type "
                + "(AddMarketDataKernel / AddTimeSeriesStore) before importing, or omit this record "
                + "type from the import — warm-start is strictly opt-in per record type.");

        var snapshotsRead = 0;
        var appendCallCount = 0;

        await foreach (var snapshot in _source.EnumerateAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshotsRead++;

            // A whole-set request key omits the per-row discriminant; appending to it
            // would overwrite distinct records sharing a date. Require the live fan-out path.
            if (snapshot.Request is FactorSeriesRequest or YieldCurveQuoteRequest
                or FuturesSettlementRequest or OptionChainRequest)
            {
                throw new NotSupportedException(
                    $"Legacy snapshots for '{typeof(TRequest).Name}' require per-row dataset-key projection; "
                    + "use the live pipeline fan-out path instead of the single-key warm-start importer.");
            }

            // Canonical rows may have no partition identity. Splitting the request cannot
            // reconstruct which payload row belongs to which key; require site attribution.
            var batchInterface = Array.Find(snapshot.Request.GetType().GetInterfaces(),
                type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IBatchDataRequest<>));
            if (batchInterface?.GetProperty(nameof(IBatchDataRequest<object>.PartitionKeys))!
                    .GetValue(snapshot.Request) is IEnumerable keys
                && keys.Cast<object>().Skip(1).Any())
            {
                throw new ArgumentException(
                    "Legacy snapshots must contain a single partition. Supply separately attributed snapshots before importing.");
            }

            // Project the same dataset key the live pipeline reads/writes for this request, then
            // append the snapshot's canonical records once per snapshot (empty payloads are a no-op).
            var datasetKey = _keyBuilder.Build(snapshot.Request);
            await store.AppendAsync(datasetKey, snapshot.Records, cancellationToken).ConfigureAwait(false);
            appendCallCount++;

            // No importer-side dedup: idempotency is the store's responsibility. Re-appending the
            // same (DatasetKey, Date) records on a second run collapses last-writer-wins, so a
            // subsequent GetRangeAsync returns the identical set — no doubling.
        }

        return new ImportLegacyL2SnapshotsResult(snapshotsRead, appendCallCount);
    }
}
