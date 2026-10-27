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

using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Results;

namespace Boutquin.MarketData.Abstractions.Contracts;

/// <summary>
/// Generic adapter contract for fetching data from an external provider and returning canonical records with provenance.
/// </summary>
/// <typeparam name="TRequest">The type of data request this adapter can handle.</typeparam>
/// <typeparam name="TRecord">The canonical record type produced by this adapter.</typeparam>
/// <remarks>
/// Each adapter is responsible for a single provider and emits strongly-typed canonical records,
/// not raw dictionaries. The pipeline calls <see cref="CanHandle"/> to route requests
/// and <see cref="FetchAsync"/> to retrieve data. Adapters must populate
/// <see cref="DataProvenance"/> in the returned envelope to support auditability and
/// license-aware fallback decisions.
/// </remarks>
public interface IDataSourceAdapter<in TRequest, TRecord>
    where TRequest : IDataRequest
{
    /// <summary>
    /// Unique key identifying the data provider (e.g., <c>new ProviderCode("tiingo")</c>).
    /// </summary>
    /// <remarks>
    /// Provider identity uses <see cref="ProviderCode"/> value equality. Implementations may
    /// construct the value when this property is read; registrations, lookups, and provenance
    /// agree when their normalized provider-code values are equal.
    /// </remarks>
    ProviderCode ProviderKey { get; }

    /// <summary>
    /// Determines whether this adapter can handle the specified request.
    /// </summary>
    /// <param name="request">The data request to evaluate.</param>
    /// <returns><see langword="true"/> if this adapter supports the request; otherwise, <see langword="false"/>.</returns>
    bool CanHandle(TRequest request);

    /// <summary>
    /// Fetches data for the specified request from the external provider.
    /// </summary>
    /// <param name="request">The data request describing what to fetch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="DataEnvelope{TPayload}"/> wrapping the fetched records with metadata.</returns>
    Task<DataEnvelope<IReadOnlyList<TRecord>>> FetchAsync(TRequest request, CancellationToken cancellationToken = default);
}
