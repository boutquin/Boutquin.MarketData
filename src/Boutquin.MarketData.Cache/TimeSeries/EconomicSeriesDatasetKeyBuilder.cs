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

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Requests;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Builds a <see cref="ScalarObservationDatasetKey"/> from an
/// <see cref="EconomicSeriesRequest"/>. The provider is resolved at the adapter-selection
/// layer (FRED for US series, Bank of Canada for CA, etc.) and supplied here via
/// <see cref="ProviderName"/>.
/// </summary>
/// <param name="ProviderName">Stable string identifier for the upstream source (e.g., <c>"fred"</c>).</param>
/// <remarks>
/// The <c>EconomicSeriesRequest.Exchange</c> hint is intentionally NOT folded into the key —
/// an economic series belongs to exactly one issuing authority. Regional-source routing happens
/// upstream at adapter selection; the cache partition is purely
/// <c>(ProviderName, SeriesId.Value)</c>.
/// </remarks>
public sealed record EconomicSeriesDatasetKeyBuilder(string ProviderName)
    : IDatasetKeyBuilder<EconomicSeriesRequest, ScalarObservationDatasetKey>
{
    /// <inheritdoc />
    public ScalarObservationDatasetKey Build(EconomicSeriesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ScalarObservationDatasetKey(ProviderName, request.SeriesId.Value);
    }
}
