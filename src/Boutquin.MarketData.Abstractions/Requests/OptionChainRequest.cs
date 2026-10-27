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
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>
/// Requests the option chain (all listed contracts) for one underlying over an observation-date range.
/// </summary>
/// <param name="Underlying">The underlying ticker — <c>Symbol.Ticker</c> of the underlying security.</param>
/// <param name="Range">The inclusive observation-date range for the request.</param>
/// <remarks>
/// <para><b>Placeholder shape.</b> This is the minimal request that compiles
/// and round-trips so the option-quote time-series tier wires end-to-end. The full shape — and whether
/// it batches multiple underlyings the way <see cref="FxHistoryRequest"/> batches pairs — is owned by
/// Q19 and finalised when an option-chain adapter is selected; no adapter feeds it yet, so the tier is
/// infrastructure-ready, data-pending.</para>
/// <para><b>Identity scoping.</b> One request covers a whole chain, so <c>Expiry</c>/<c>Strike</c>/
/// <c>Type</c> are NOT request fields — they are per-contract discriminants that live in
/// <see cref="Keys.OptionQuoteDatasetKey"/> and are filtered at the read/builder layer.
/// <c>Provider</c> is likewise not a request field — it is stamped by the responding adapter.</para>
/// </remarks>
public sealed record OptionChainRequest(
    string Underlying,
    DateRange Range) : IDataRequest
{
    /// <summary>
    /// Optional venue hint — mirrors <see cref="YieldCurveQuoteRequest.Exchange"/>; lets adapter
    /// orchestration prefer a co-located source. When <see langword="null"/> any adapter may serve
    /// the request.
    /// </summary>
    public ExchangeCode? Exchange { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Underlying slashes are doubled before appending an optional <c>/</c> plus
    /// <see cref="Exchange"/> suffix. Slash-free underlyings retain their legacy keys.
    /// Upgrade using a fresh snapshot directory and restart to discard old L1 entries: legacy
    /// keys can contain aliased data even at an unchanged venue-qualified key. Re-fetch the data;
    /// no automatic migration or legacy-key fallback is performed. Existing files are left untouched.
    /// </remarks>
    public string DatasetKey =>
        DividendDatasetIdentity.EncodeTickerAndExchange(Underlying, Exchange);

    /// <summary>
    /// Option quotes are daily observations.
    /// </summary>
    public DataFrequency Frequency => DataFrequency.Daily;

    /// <inheritdoc />
    /// <remarks>
    /// Preserves the <see cref="Underlying"/> and the optional <see cref="Exchange"/> venue hint;
    /// only <see cref="IDataRequest.Range"/> is narrowed to the gap. Used by the time-series
    /// gap-fill to fetch just a missing sub-range of the chain's observation window.
    /// </remarks>
    public IDataRequest WithRange(DateRange range) =>
        new OptionChainRequest(Underlying, range) { Exchange = Exchange };
}
