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

using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Abstractions.Snapshots;

namespace Boutquin.MarketData.Abstractions.Contracts;

/// <summary>
/// Orchestrates the end-to-end data fetch pipeline, coordinating adapters, caching, normalization, and provenance tracking.
/// </summary>
/// <remarks>
/// The pipeline flow is: check cache &#x2192; select adapter &#x2192; partition request &#x2192;
/// fetch via transport &#x2192; normalize &#x2192; assemble coverage and provenance &#x2192; store snapshot.
/// Callers receive a single <see cref="DataEnvelope{TPayload}"/> regardless of how many
/// adapters or partitions were involved.
/// </remarks>
public interface IDataPipeline
{
    /// <summary>
    /// Runs the full pipeline for the specified request, returning an envelope containing the fetched
    /// records together with coverage, issues, and provenance metadata.
    /// </summary>
    /// <typeparam name="TRequest">The type of data request.</typeparam>
    /// <typeparam name="TRecord">The canonical record type returned by the pipeline.</typeparam>
    /// <param name="request">The data request describing what to fetch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="DataEnvelope{TPayload}"/> wrapping the fetched records with metadata.</returns>
    Task<DataEnvelope<IReadOnlyList<TRecord>>> ExecuteAsync<TRequest, TRecord>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IDataRequest;

    /// <summary>
    /// Runs a batch request one partition at a time and returns the results <b>partitioned by
    /// dataset key</b> — one <see cref="DataEnvelope{TPayload}"/> per partition keyed by the
    /// <typeparamref name="TDatasetKey"/> built for that partition.
    /// </summary>
    /// <typeparam name="TRequest">The batch request type, partitioned over <typeparamref name="TPartitionKey"/>.</typeparam>
    /// <typeparam name="TRecord">The canonical record type returned by the pipeline.</typeparam>
    /// <typeparam name="TPartitionKey">The per-partition identity (e.g. <c>FxPair</c>).</typeparam>
    /// <typeparam name="TDatasetKey">The dataset key each partition's records are grouped under.</typeparam>
    /// <param name="request">The batch request whose <see cref="IBatchDataRequest{TPartitionKey}.PartitionKeys"/> are run individually.</param>
    /// <param name="splitForPartition">Projects the batch request down to a single-partition request for one partition key.</param>
    /// <param name="buildDatasetKey">Builds the dataset key a partition's records are grouped under.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A dictionary mapping each partition's dataset key to its own envelope.</returns>
    /// <remarks>
    /// This is a per-pair FX read: once the currency pair
    /// leaves the <c>FxRate</c> body, a multi-pair <c>FxHistoryRequest</c> can no longer be
    /// demultiplexed by a row field, so the pipeline returns per-dataset-key partitioned
    /// envelopes and the consumer keys on the dataset key — where the pair identity now lives.
    /// Each partition runs through the full <see cref="ExecuteAsync{TRequest, TRecord}"/> path
    /// (cache cascade, adapter fallback, normalization, coverage), so the partitioned read is the
    /// real fetch path, not a parallel implementation.
    /// </remarks>
    Task<IReadOnlyDictionary<TDatasetKey, DataEnvelope<IReadOnlyList<TRecord>>>>
        ExecutePartitionedAsync<TRequest, TRecord, TPartitionKey, TDatasetKey>(
            TRequest request,
            Func<TRequest, TPartitionKey, TRequest> splitForPartition,
            Func<TRequest, TPartitionKey, TDatasetKey> buildDatasetKey,
            CancellationToken cancellationToken = default)
        where TRequest : IBatchDataRequest<TPartitionKey>
        where TDatasetKey : notnull;

    /// <summary>
    /// Acquires a read handle whose first read for each record type captures that type's
    /// engine watermark. Later reads of that type reuse the captured view; creating the
    /// handle does not capture engine state. This is not a cross-record-type atomic snapshot.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A fresh snapshot handle wired to the pipeline's time-series store registry.</returns>
    /// <remarks>
    /// Only columnar-rehydratable record types with a registered snapshot binding are supported.
    /// Row-only <c>DividendPayment</c> and <c>FuturesSettlement</c> do not satisfy that constraint.
    /// A row-format store for a columnar-rehydratable type can still participate. Different
    /// record types may capture different watermarks; writes after handle creation but before
    /// a type's first read can be visible to that type.
    /// Consumers MUST dispose the handle via <c>await using</c> — a GC-finalised handle leaves its
    /// segment pins in place across compaction until the finaliser runs. Unlike the live pipeline,
    /// a snapshot read fails fast with <see cref="SnapshotGapException"/> on a gap rather than
    /// dispatching an adapter.
    /// </remarks>
    Task<IDataSnapshot> AcquireReadSnapshotAsync(CancellationToken cancellationToken = default);
}
