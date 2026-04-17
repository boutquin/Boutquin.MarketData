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
/// Marker interface for data requests that operate on multiple partition keys (e.g., multiple symbols or currency pairs).
/// </summary>
/// <typeparam name="TPartitionKey">The type of each partition key (e.g., <see langword="string"/> for ticker symbols, <see cref="Records.FxPair"/> for currency pairs).</typeparam>
/// <remarks>
/// The pipeline uses this interface to detect batch requests and enable per-partition
/// checkpointing. Single-key requests (e.g., <c>EconomicSeriesRequest</c>) do not
/// implement this interface and are treated as a single partition.
/// </remarks>
public interface IBatchDataRequest<out TPartitionKey> : IDataRequest
{
    /// <summary>
    /// The partition keys for this batch request.
    /// </summary>
    IReadOnlyList<TPartitionKey> PartitionKeys { get; }
}
