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

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Builds the per-record-type <c>DatasetKey</c> for a concrete request. One implementation
/// ships per <c>(TRequest, TDatasetKey)</c> pair; <c>DataPipeline</c> resolves the correct
/// builder by request type.
/// </summary>
/// <typeparam name="TRequest">The concrete request record (e.g., <c>EconomicSeriesRequest</c>).</typeparam>
/// <typeparam name="TDatasetKey">The dataset key produced (e.g., <c>ScalarObservationDatasetKey</c>).</typeparam>
/// <remarks>
/// <para><b>Shape:</b> each builder is stateless and trivially implementable — it takes
/// a concrete request record and projects its identity fields into the dataset key. Stringifying
/// enums (<c>Frequency.ToString()</c>) and typed-string wrappers at this boundary keeps the
/// DatasetKey structs primitive-kind-only, letting the Boutquin.Storage source generator emit
/// their <c>ISerializable</c> without custom handlers.</para>
/// <para><b>Adapter author responsibility:</b> adapters introducing a new request type provide
/// a matching <see cref="IDatasetKeyBuilder{TRequest, TDatasetKey}"/> implementation. The builder
/// is registered in the DI container alongside the adapter.</para>
/// <para><b>Non-goals:</b> this interface does not handle batched requests that produce multiple
/// dataset keys (e.g., <c>PriceHistoryRequest</c> with multiple symbols). Batch decomposition
/// happens at the pipeline layer, which iterates the batch's partition keys and calls a
/// single-identity builder per element.</para>
/// </remarks>
public interface IDatasetKeyBuilder<in TRequest, out TDatasetKey>
    where TRequest : IDataRequest
{
    /// <summary>Builds the dataset key for the supplied request.</summary>
    TDatasetKey Build(TRequest request);
}
