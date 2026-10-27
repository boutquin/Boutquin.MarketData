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
/// Per-contract fan-out for an option chain — a <b>keyed-row envelope</b>, the
/// fetched-differs-from-stored case. A single <see cref="OptionChainRequest"/> returns one chain's
/// N co-dated contract rows as
/// <see cref="OptionChainRow"/>s (<c>TFetched</c>); each must be its own
/// <see cref="OptionQuoteDatasetKey"/> series <c>(Provider, Underlying, Expiry, Strike, Type)</c>, and
/// the slim <see cref="OptionQuote"/> body (<c>TStored</c>) is written under it — never colliding under
/// one blanked-discriminant key.
/// </summary>
/// <remarks>
/// <para><b>Keyed-row projection (fetched ≠ stored).</b> Unlike the body-discriminant fan-outs
/// (<see cref="YieldCurveQuoteFanOut"/>, <see cref="FactorObservationFanOut"/>) whose fetched and
/// stored types coincide, the contract identity here is carried <em>beside</em> the body in the fetched
/// <see cref="OptionChainRow"/> — the record body <see cref="OptionQuote"/> carries no
/// <c>Strike</c>/<c>Expiry</c>/<c>Type</c>. <see cref="Project"/> reads the identity from
/// <see cref="OptionChainRow.Contract"/> to build the fully-populated key and stores the slim
/// <see cref="OptionChainRow.Quote"/>.</para>
/// <para><b>Prefix scan.</b> <see cref="PrefixBounds"/> spans
/// <c>(Provider, Underlying, min, 0, Call)</c> through <c>(Provider, Underlying+U+FFFF, min, 0, Call)</c>
/// — appending the U+FFFF sentinel to the <em>Underlying</em> makes the upper bound sort strictly after
/// every real <c>(Provider, Underlying, *)</c> contract (an underlying never contains U+FFFF), so the
/// whole chain is one contiguous range regardless of expiry/strike/type. The bound is a comparison key
/// only (never persisted), and <see cref="KeyMatchesRequest"/> drops any over-scan into an adjacent
/// underlying, so the sentinel never admits a foreign row.</para>
/// </remarks>
/// <param name="ProviderName">
/// The provider column stamped into every produced key — the same value the registered
/// <see cref="OptionQuoteDatasetKeyBuilder"/> uses, so append keys and read-scan bounds agree.
/// </param>
public sealed record OptionChainRowFanOut(string ProviderName)
    : IDatasetKeyFanOut<OptionChainRequest, OptionQuoteDatasetKey, OptionChainRow, OptionQuote>
{
    // Sorts strictly after any real underlying ticker; a comparison bound only, never serialized/stored.
    private const string UnderlyingUpperSentinel = "￿";

    /// <inheritdoc />
    public (OptionQuoteDatasetKey Key, OptionQuote Record) Project(
        OptionChainRequest request, OptionChainRow row)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(row);
        var c = row.Contract;
        return (
            new OptionQuoteDatasetKey(ProviderName, request.Underlying, c.Expiry, c.Strike, c.Type),
            row.Quote);
    }

    /// <inheritdoc />
    public (OptionQuoteDatasetKey Lo, OptionQuoteDatasetKey Hi) PrefixBounds(OptionChainRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (
            new OptionQuoteDatasetKey(
                ProviderName, request.Underlying, DateOnly.MinValue, 0m, OptionType.Call),
            new OptionQuoteDatasetKey(
                ProviderName, request.Underlying + UnderlyingUpperSentinel, DateOnly.MinValue, 0m, OptionType.Call));
    }

    /// <inheritdoc />
    public bool KeyMatchesRequest(OptionChainRequest request, OptionQuoteDatasetKey key)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.Equals(key.Provider, ProviderName, StringComparison.Ordinal)
            && string.Equals(key.Underlying, request.Underlying, StringComparison.Ordinal);
    }
}
