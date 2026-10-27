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
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Builds an <see cref="FxRateDatasetKey"/> from an <see cref="FxHistoryRequest"/>. The
/// currency pair is part of the dataset-key identity rather than a row field,
/// so the builder expands an <see cref="FxPair"/> into the two <c>Currency</c> enum key columns.
/// </summary>
/// <param name="ProviderName">Adapter-assigned provider identifier (e.g. <c>"frankfurter"</c>, <c>"boc"</c>).</param>
/// <remarks>
/// <para><b>Multi-pair batches.</b> An <see cref="FxHistoryRequest"/> may carry several pairs.
/// <see cref="Build"/> resolves the single-pair case (exactly one <see cref="FxPair"/>); the
/// pipeline's per-pair partitioned read calls <see cref="BuildForPair"/> with each pair so every
/// pair maps to its own <see cref="FxRateDatasetKey"/>. The pair identity now lives in the key,
/// which is how the consumer demultiplexes a multi-pair result.</para>
/// </remarks>
public sealed record FxRateDatasetKeyBuilder(string ProviderName)
    : IDatasetKeyBuilder<FxHistoryRequest, FxRateDatasetKey>
{
    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// The request carries zero or more than one pair — a single-pair key cannot be built; use
    /// <see cref="BuildForPair"/> per pair for a multi-pair batch.
    /// </exception>
    public FxRateDatasetKey Build(FxHistoryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Pairs.Count != 1)
        {
            throw new ArgumentException(
                $"FxRateDatasetKeyBuilder.Build requires exactly one pair (got {request.Pairs.Count}); " +
                "use BuildForPair per pair for a multi-pair batch.",
                nameof(request));
        }

        return BuildForPair(request, request.Pairs[0]);
    }

    /// <summary>
    /// Builds the dataset key for a single <paramref name="pair"/> of a (possibly multi-pair)
    /// <paramref name="request"/>, expanding the pair into the two <c>Currency</c> key columns.
    /// </summary>
    public FxRateDatasetKey BuildForPair(FxHistoryRequest request, FxPair pair)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(pair);

        return new FxRateDatasetKey(
            ProviderName,
            pair.BaseCurrency,
            pair.QuoteCurrency,
            request.Frequency);
    }
}
