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
/// Builds a <see cref="FactorObservationDatasetKey"/> from a
/// <see cref="FactorSeriesRequest"/>. Under Storage 2.0 the key carries the
/// <see cref="DataFrequency"/> enum directly (no <c>ToString()</c> stringify at the boundary).
/// </summary>
/// <remarks>
/// <para><b>FactorName placeholder (M-CR / Q13):</b> commit <c>47ed89c</c> reshaped
/// <see cref="Boutquin.MarketData.Abstractions.Records.FactorObservation"/> to one row per factor
/// and <see cref="FactorObservationDatasetKey"/> gained a <c>FactorName</c> discriminant so each
/// factor is its own series. A <see cref="FactorSeriesRequest"/>, however, addresses a whole factor
/// <i>set</i> (e.g. Fama-French 3-factor) — the individual factor name is per-row data known only at
/// normalization, not a request input. This single-key projection therefore leaves
/// <c>FactorName</c> empty, exactly as <see cref="FuturesSettlementDatasetKeyBuilder"/> leaves
/// <c>ContractMonth</c> empty: per-factor fan-out (one key per factor) is a pipeline-layer
/// concern implemented by the time-series read path, not at this single-identity builder.</para>
/// </remarks>
public sealed record FactorSeriesDatasetKeyBuilder(string ProviderName)
    : IDatasetKeyBuilder<FactorSeriesRequest, FactorObservationDatasetKey>
{
    /// <inheritdoc />
    public FactorObservationDatasetKey Build(FactorSeriesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new FactorObservationDatasetKey(
            ProviderName,
            request.DatasetName.Value,
            request.Frequency,
            FactorName: string.Empty);
    }
}
