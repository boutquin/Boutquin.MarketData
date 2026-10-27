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

using Boutquin.MarketData.Abstractions.Exceptions;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Projects each futures settlement into its own delivery-month series and scans all months
/// for the requested product without collapsing co-dated contracts.
/// </summary>
/// <remarks>
/// The provider must match the registered <see cref="FuturesSettlementDatasetKeyBuilder"/>.
/// Prefix bounds are comparison keys only. Legacy blank-month data is rejected because earlier
/// writes may already have overwritten other maturities; use a fresh store and re-ingest from source.
/// Instances are immutable and safe for concurrent use.
/// </remarks>
/// <param name="ProviderName">Provider column shared by appended keys and scan bounds.</param>
public sealed record FuturesSettlementFanOut(string ProviderName)
    : IDatasetKeyFanOut<FuturesSettlementRequest, FuturesSettlementDatasetKey, FuturesSettlement, FuturesSettlement>
{
    /// <inheritdoc />
    public (FuturesSettlementDatasetKey Key, FuturesSettlement Record) Project(
        FuturesSettlementRequest request, FuturesSettlement row)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (new FuturesSettlementDatasetKey(ProviderName, request.ProductCode.Value, row.ContractMonth.ToString()), row);
    }

    /// <inheritdoc />
    public (FuturesSettlementDatasetKey Lo, FuturesSettlementDatasetKey Hi) PrefixBounds(FuturesSettlementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (
            new FuturesSettlementDatasetKey(ProviderName, request.ProductCode.Value, string.Empty),
            new FuturesSettlementDatasetKey(ProviderName, request.ProductCode.Value, "\uffff"));
    }

    /// <inheritdoc />
    /// <exception cref="MarketDataStorageException">
    /// A matching product contains legacy blank-month data; re-ingestion into a fresh store is required.
    /// </exception>
    public bool KeyMatchesRequest(FuturesSettlementRequest request, FuturesSettlementDatasetKey key)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(key.Provider, ProviderName, StringComparison.Ordinal)
            || !string.Equals(key.ContractCode, request.ProductCode.Value, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrEmpty(key.ContractMonth))
        {
            throw new MarketDataStorageException(
                "Futures time-series data uses the legacy blank contract-month key. Use a fresh store and re-ingest "
                + "from source; overwritten maturities cannot be recovered from the collapsed data. "
                + "See docs/time-series-cache.md#futures-contract-month-migration.");
        }

        return true;
    }
}
