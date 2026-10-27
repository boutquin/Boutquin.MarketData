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
/// Builds a <see cref="PriceBarDatasetKey"/> from a <see cref="PriceHistoryRequest"/>. The
/// provider is resolved at the adapter-selection layer (Tiingo, TwelveData, …) and supplied here
/// via <see cref="ProviderName"/>; the frequency and optional listing venue come straight off the
/// request. The time-series pipeline uses this request → dataset-key projection to
/// resolve the live <c>Bar</c> store and compute gaps.
/// </summary>
/// <param name="ProviderName">Stable string identifier for the upstream source (e.g., <c>"tiingo"</c>).</param>
/// <remarks>
/// <para>
/// A <see cref="PriceHistoryRequest"/> carries a list of <c>Symbols</c> because it is also a
/// batch request; the pipeline partitions a multi-symbol batch into one single-symbol request per
/// partition <b>before</b> reaching the time-series store, so <see cref="Build"/> keys on the
/// request's first symbol (the partition's ticker). A request with no symbols is a programming
/// error and throws.
/// </para>
/// <para>
/// The key mirrors the request's <c>Frequency</c> (both <c>DataFrequency</c> — no
/// <c>BarGranularity</c> substitution) and <c>Exchange</c>
/// (<c>ExchangeCode?</c>, null-neutral), so a per-venue request naturally partitions
/// into its own LSM key range.
/// </para>
/// </remarks>
public sealed record PriceBarDatasetKeyBuilder(string ProviderName)
    : IDatasetKeyBuilder<PriceHistoryRequest, PriceBarDatasetKey>
{
    /// <inheritdoc />
    public PriceBarDatasetKey Build(PriceHistoryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Symbols.Count == 0)
        {
            throw new ArgumentException(
                "A PriceHistoryRequest reaching the time-series store must carry exactly one symbol "
                + "(the pipeline partitions multi-symbol batches per partition before key building); "
                + "the request carried none.",
                nameof(request));
        }

        return new PriceBarDatasetKey(
            ProviderName,
            request.Frequency,
            request.Symbols[0].Ticker,
            request.Exchange);
    }
}
