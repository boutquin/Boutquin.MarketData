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
/// Requests yield curve quotes (tenor/rate pairs) for a specific curve on a single date.
/// </summary>
/// <param name="CurveId">The curve identifier (e.g., <c>new YieldCurveId("CAD-ZERO")</c>).</param>
/// <param name="AsOfDate">The observation date for the curve snapshot.</param>
public sealed record YieldCurveQuoteRequest(
    YieldCurveId CurveId,
    DateOnly AsOfDate) : IDataRequest
{
    /// <summary>
    /// Optional regional hint — yield curves are currency-specific but often have multiple
    /// publishing venues (Bank of Canada for CAD, US Treasury for USD, ECB for EUR). The
    /// Exchange code lets adapter orchestration prefer a co-located source.
    /// </summary>
    /// <remarks>
    /// Exposed as an <c>init</c> property for 1.1.0 binary compatibility with 1.0.x consumers.
    /// </remarks>
    public ExchangeCode? Exchange { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Identifier slashes are doubled before appending an optional <c>/</c> plus
    /// <see cref="Exchange"/> suffix. Slash-free identifiers retain their legacy keys.
    /// Upgrade using a fresh snapshot directory and restart to discard old L1 entries: legacy
    /// keys can contain aliased data even at an unchanged venue-qualified key. Re-fetch the data;
    /// no automatic migration or legacy-key fallback is performed. Existing files are left untouched.
    /// </remarks>
    public string DatasetKey =>
        DividendDatasetIdentity.EncodeTickerAndExchange(CurveId.Value, Exchange);

    /// <summary>
    /// Single-day range for the as-of date.
    /// </summary>
    public DateRange Range => new(AsOfDate, AsOfDate);

    /// <summary>
    /// Yield curves are daily observations.
    /// </summary>
    public DataFrequency Frequency => DataFrequency.Daily;

    /// <inheritdoc />
    /// <remarks>
    /// A yield-curve request is a single-day snapshot whose <see cref="Range"/> is derived from
    /// <see cref="AsOfDate"/>, so the only coverage gap the time-series pipeline can
    /// produce for it is that single day. This sets <see cref="AsOfDate"/> from the gap's start
    /// (<c>range.From</c> equals <c>range.To</c> for a single-day gap) and preserves the
    /// <see cref="CurveId"/> and the optional <see cref="Exchange"/> venue hint.
    /// </remarks>
    public IDataRequest WithRange(DateRange range) =>
        new YieldCurveQuoteRequest(CurveId, range.From) { Exchange = Exchange };
}
