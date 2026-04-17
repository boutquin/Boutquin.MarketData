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
}
