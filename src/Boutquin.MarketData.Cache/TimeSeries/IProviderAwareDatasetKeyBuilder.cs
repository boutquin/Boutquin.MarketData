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
using Boutquin.MarketData.Abstractions.Provenance;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Builds a physical dataset key after fallback has selected the adapter that owns the records.
/// </summary>
/// <typeparam name="TRequest">The concrete request type.</typeparam>
/// <typeparam name="TDatasetKey">The physical dataset-key type.</typeparam>
public interface IProviderAwareDatasetKeyBuilder<in TRequest, out TDatasetKey>
    where TRequest : IDataRequest
{
    /// <summary>Builds a provider-scoped key for the supplied request.</summary>
    TDatasetKey Build(TRequest request, ProviderCode provider);
}
