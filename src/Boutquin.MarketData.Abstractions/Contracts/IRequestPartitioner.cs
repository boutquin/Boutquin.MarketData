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
/// Splits a large data request into smaller sub-requests to respect API limits or improve parallelism.
/// </summary>
/// <typeparam name="TRequest">The type of data request to partition.</typeparam>
/// <remarks>
/// Partitioning strategies include splitting by date range (e.g., monthly chunks for APIs
/// with row-count limits) or by dataset key (e.g., batching multiple symbols). The pipeline
/// reassembles partitioned results into a single <see cref="Results.DataEnvelope{TPayload}"/> before
/// returning to the caller.
/// </remarks>
public interface IRequestPartitioner<TRequest>
    where TRequest : IDataRequest
{
    /// <summary>
    /// Partitions the specified request into one or more smaller sub-requests.
    /// </summary>
    /// <param name="request">The original request to split.</param>
    /// <returns>A list of sub-requests that together cover the original request's scope.</returns>
    IReadOnlyList<TRequest> Partition(TRequest request);
}
