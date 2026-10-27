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

using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Per-row-discriminant fan-out contract for a <b>multi-row-per-date</b> record type.
/// A single request can legitimately
/// return N co-dated rows that must each live under their own per-discriminant <c>DatasetKey</c>
/// series — <c>YieldCurveQuote</c> by <c>Tenor</c>, <c>FactorObservation</c> by <c>FactorName</c>,
/// <c>OptionQuote</c> by <c>(Expiry, Strike, Type)</c>. This contract owns the <b>append</b> side
/// (project each fetched row to its fully-populated key + stored record) and the <b>read</b> side's
/// key structure (the <c>(prefix, *)</c> scan bounds and the request-membership predicate).
/// </summary>
/// <typeparam name="TRequest">The concrete request record.</typeparam>
/// <typeparam name="TDatasetKey">The fully-populated per-discriminant dataset key.</typeparam>
/// <typeparam name="TFetched">
/// The fetched-row type the adapter emits. It is <b>not</b> assumed equal to
/// <typeparamref name="TStored"/>: body-discriminant types (<c>YieldCurveQuote</c>,
/// <c>FactorObservation</c>, <c>FuturesSettlement</c>) use the identity projection
/// (<c>TFetched == TStored</c>, discriminant read from the record body), while the keyed-row
/// envelope plugs in <c>OptionChainRow → (OptionQuoteDatasetKey, OptionQuote)</c> with no mechanism
/// rework.
/// </typeparam>
/// <typeparam name="TStored">The dated record actually written to the store.</typeparam>
/// <remarks>
/// <para><b>Projection contract.</b> The mechanism is
/// <c>fetched-row → (fully-populated DatasetKey, stored record)</c> — deliberately NOT a
/// <c>Func&lt;TRequest, TRecord, TKey&gt;</c> keyed to the stored record only, which could never fit
/// a fetched type that differs from the stored type (the OptionQuote case). Keeping the fetched and
/// stored types distinct lets keyed-row envelopes reuse this mechanism unchanged.</para>
/// <para><b>Read-side prefix scan.</b> The per-discriminant keys of one logical entity sort
/// contiguously by their shared prefix (e.g. <c>(Provider, CurveId, *)</c>), so a curve-as-of read is
/// a prefix range scan across the store's composite keys, merged and returned in dataset-key order.
/// <see cref="PrefixBounds"/> supplies the inclusive lower/upper key bounds and
/// <see cref="KeyMatchesRequest"/> filters any over-scan into an adjacent entity.</para>
/// </remarks>
public interface IDatasetKeyFanOut<TRequest, TDatasetKey, TFetched, TStored>
    where TRequest : IDataRequest
    where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    where TStored : ISerializable<TStored>, IHasDate, new()
{
    /// <summary>
    /// Projects one fetched row into its fully-populated per-discriminant dataset key and the stored
    /// record written under it. The append path groups by <see cref="System.ValueTuple{T1, T2}"/>
    /// <c>Key</c> and appends the co-keyed records per key.
    /// </summary>
    /// <param name="request">The request the row was fetched for (supplies the key prefix columns).</param>
    /// <param name="row">A single fetched row.</param>
    /// <returns>The fully-populated dataset key and the record to store under it.</returns>
    (TDatasetKey Key, TStored Record) Project(TRequest request, TFetched row);

    /// <summary>
    /// The inclusive lower/upper dataset-key bounds of the <c>(prefix, *)</c> range that spans every
    /// per-discriminant key for <paramref name="request"/>'s logical entity, for the read-side cross-key
    /// scan. The upper bound may over-scan into an adjacent entity; <see cref="KeyMatchesRequest"/> is
    /// the authoritative membership filter.
    /// </summary>
    /// <param name="request">The request whose entity's key range is bounded.</param>
    /// <returns>The inclusive <c>Lo</c>/<c>Hi</c> dataset-key bounds for the prefix scan.</returns>
    (TDatasetKey Lo, TDatasetKey Hi) PrefixBounds(TRequest request);

    /// <summary>
    /// Whether <paramref name="key"/> belongs to <paramref name="request"/>'s logical entity — the
    /// authoritative membership filter applied to every key the prefix scan returns.
    /// </summary>
    /// <param name="request">The request defining the entity.</param>
    /// <param name="key">A dataset key returned by the prefix scan.</param>
    /// <returns><see langword="true"/> when the key shares the request's prefix; otherwise <see langword="false"/>.</returns>
    bool KeyMatchesRequest(TRequest request, TDatasetKey key);
}
