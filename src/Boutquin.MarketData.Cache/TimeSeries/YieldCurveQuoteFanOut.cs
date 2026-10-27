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
/// Per-tenor fan-out for <see cref="YieldCurveQuote"/> — the body-<c>Tenor</c> discriminant
/// identifies each independent tenor series. A single
/// <see cref="YieldCurveQuoteRequest"/> returns one curve snapshot's N tenor nodes co-dated on the
/// as-of date; each node must be its own <see cref="YieldCurveQuoteDatasetKey"/> series
/// <c>(Provider, CurveId, Tenor)</c> rather than colliding under one blanked-<c>Tenor</c> key.
/// </summary>
/// <remarks>
/// <para><b>Identity projection.</b> The fetched and stored types are both
/// <see cref="YieldCurveQuote"/>; the discriminant is read straight off the record body
/// (<see cref="YieldCurveQuote.Tenor"/>, Tag 2). This is the trivial case of the
/// <see cref="IDatasetKeyFanOut{TRequest, TDatasetKey, TFetched, TStored}"/> shape, whose four
/// distinct type parameters exist so the <c>OptionChainRow → (OptionQuoteDatasetKey, OptionQuote)</c>
/// envelope plugs in without reworking the mechanism.</para>
/// <para><b>Prefix scan.</b> <see cref="PrefixBounds"/> spans <c>(Provider, CurveId, "")</c> through
/// <c>(Provider, CurveId + U+FFFF, "")</c>. Extending the curve identifier makes the upper
/// bound sort after every tenor under the original identifier, including arbitrary-length
/// Unicode labels. Bounds are comparison keys only (never persisted). The range can include
/// adjacent curves; <see cref="KeyMatchesRequest"/> filters them by exact provider and curve.</para>
/// </remarks>
/// <param name="ProviderName">
/// The provider column stamped into every produced key — the same value the registered
/// <see cref="YieldCurveQuoteDatasetKeyBuilder"/> uses, so append keys and read-scan bounds agree.
/// </param>
public sealed record YieldCurveQuoteFanOut(string ProviderName)
    : IDatasetKeyFanOut<YieldCurveQuoteRequest, YieldCurveQuoteDatasetKey, YieldCurveQuote, YieldCurveQuote>
{
    /// <inheritdoc />
    public (YieldCurveQuoteDatasetKey Key, YieldCurveQuote Record) Project(
        YieldCurveQuoteRequest request, YieldCurveQuote row)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The discriminant is canonicalised HERE, unconditionally — not inherited from whatever
        // YieldCurveQuoteNormalizer may or may not have done to the body. Deriving the key from the
        // un-canonicalised body column made the persisted key depend on the
        // RegisterDefaultNormalizers flag, so flipping it over a populated store forked one tenor
        // into a raw-era key and a normalized-era key.
        return (
            new YieldCurveQuoteDatasetKey(
                ProviderName,
                request.DatasetKey,
                YieldCurveQuoteDatasetKey.CanonicalTenor(row.Tenor)),
            row);
    }

    /// <inheritdoc />
    public (YieldCurveQuoteDatasetKey Lo, YieldCurveQuoteDatasetKey Hi) PrefixBounds(
        YieldCurveQuoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (
            new YieldCurveQuoteDatasetKey(ProviderName, request.DatasetKey, string.Empty),
            new YieldCurveQuoteDatasetKey(ProviderName, request.DatasetKey + "\uFFFF", string.Empty));
    }

    /// <inheritdoc />
    /// <exception cref="Abstractions.Exceptions.MarketDataStorageException">
    /// <paramref name="key"/> belongs to this request's curve but its <c>Tenor</c> discriminant is in
    /// the pre-canonicalisation encoding — the store directory was populated by a build that
    /// projected the raw body column. Merging it would double-count every date the legacy and
    /// canonical keys share, so it is reported instead of merged; see
    /// <see href="docs/time-series-cache.md#migrating-a-store-written-before-the-canonicalisation">Migrating
    /// a store written before the canonicalisation</see> for the remedy.
    /// </exception>
    public bool KeyMatchesRequest(YieldCurveQuoteRequest request, YieldCurveQuoteDatasetKey key)
    {
        ArgumentNullException.ThrowIfNull(request);
        var matches = string.Equals(key.Provider, ProviderName, StringComparison.Ordinal)
            && string.Equals(key.CurveId, request.DatasetKey, StringComparison.Ordinal);

        if (!matches)
        {
            return false;
        }

        // Only keys this request would actually merge are checked: a stored key still under the raw
        // spelling was written before the canonicalisation and is NOT the same key this build now
        // projects for that series, so merging it forks the series instead of continuing it.
        LegacyDiscriminantEncodingGuard.EnsureCanonicalDiscriminant(
            nameof(YieldCurveQuote),
            nameof(YieldCurveQuoteDatasetKey.Tenor),
            key.Tenor,
            YieldCurveQuoteDatasetKey.CanonicalTenor(key.Tenor));

        return true;
    }
}
