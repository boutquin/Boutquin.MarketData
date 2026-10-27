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
/// Builds a <see cref="YieldCurveQuoteDatasetKey"/> from a
/// <see cref="YieldCurveQuoteRequest"/>. Maps <c>(Provider, CurveId, Tenor)</c>, taking the
/// <c>CurveId</c> column from <see cref="YieldCurveQuoteRequest.DatasetKey"/> — which already folds
/// the optional regional hint into <c>CurveId[/Exchange]</c> at the request layer, so no
/// separate <c>Exchange</c> key column is needed.
/// </summary>
/// <remarks>
/// <para><b>Tenor placeholder:</b> Each
/// <see cref="Boutquin.MarketData.Abstractions.Records.YieldCurveQuote"/> is a dated record for one
/// tenor, and <see cref="YieldCurveQuoteDatasetKey"/> carries a <c>Tenor</c> discriminant so each
/// tenor is its own series. A <see cref="YieldCurveQuoteRequest"/>, however, addresses a whole curve
/// snapshot for one as-of date — the individual tenor is per-row data, not a request input. This
/// single-key projection therefore leaves <c>Tenor</c> empty, exactly as the factor / futures-month
/// builders leave their per-row discriminant empty: per-tenor fan-out (one key per node) is a
/// pipeline-layer concern implemented by the time-series read path, not at this
/// single-identity builder.</para>
/// </remarks>
public sealed record YieldCurveQuoteDatasetKeyBuilder(string ProviderName)
    : IDatasetKeyBuilder<YieldCurveQuoteRequest, YieldCurveQuoteDatasetKey>
{
    /// <inheritdoc />
    public YieldCurveQuoteDatasetKey Build(YieldCurveQuoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new YieldCurveQuoteDatasetKey(
            ProviderName,
            request.DatasetKey,
            Tenor: string.Empty);
    }
}
