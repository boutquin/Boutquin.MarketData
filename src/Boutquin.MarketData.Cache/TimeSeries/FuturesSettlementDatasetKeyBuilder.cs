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
/// Builds a <see cref="FuturesSettlementDatasetKey"/> from a
/// <see cref="FuturesSettlementRequest"/>. The request carries a single product code; the
/// contract-month slice is not part of the request key and is instead carried by each
/// <see cref="Boutquin.MarketData.Abstractions.Records.FuturesSettlement.ContractMonth"/>
/// value. This builder uses an empty month as a request-level identity only;
/// <see cref="FuturesSettlementFanOut"/> supplies the full month key for persisted rows.
/// </summary>
public sealed record FuturesSettlementDatasetKeyBuilder(string ProviderName)
    : IDatasetKeyBuilder<FuturesSettlementRequest, FuturesSettlementDatasetKey>
{
    /// <inheritdoc />
    public FuturesSettlementDatasetKey Build(FuturesSettlementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Request-level identity only; the fan-out projects each persisted row to its month key.
        return new FuturesSettlementDatasetKey(
            ProviderName,
            request.ProductCode.Value,
            ContractMonth: string.Empty);
    }
}
