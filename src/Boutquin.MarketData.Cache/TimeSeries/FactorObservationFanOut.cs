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
/// Per-factor fan-out for <see cref="FactorObservation"/> — the body-<c>FactorName</c>
/// discriminant identifies each independent factor series.
/// A single <see cref="FactorSeriesRequest"/> returns one factor set's N co-dated factor
/// observations on the as-of date; each factor must be its own <see cref="FactorObservationDatasetKey"/>
/// series <c>(Provider, DatasetId, Frequency, FactorName)</c> rather than colliding under one
/// blanked-<c>FactorName</c> key.
/// </summary>
/// <remarks>
/// <para><b>Identity projection.</b> The fetched and stored types are both
/// <see cref="FactorObservation"/>; the discriminant is read straight off the record body
/// (<see cref="FactorObservation.FactorName"/>, Tag 2). This mirrors
/// <see cref="YieldCurveQuoteFanOut"/> — the trivial case of the
/// <see cref="IDatasetKeyFanOut{TRequest, TDatasetKey, TFetched, TStored}"/> shape.</para>
/// <para><b>Prefix scan.</b> <see cref="PrefixBounds"/> spans
/// <c>(Provider, DatasetId, Frequency, "")</c> through
/// <c>(Provider, DatasetId + U+FFFF, Frequency, "")</c>. Extending the dataset identifier
/// makes the upper bound sort after every factor name under the original identifier, including
/// arbitrary-length Unicode labels. Bounds are comparison keys only (never persisted).
/// The range can include other frequencies and adjacent factor sets;
/// <see cref="KeyMatchesRequest"/> filters them by exact provider, dataset, and frequency.</para>
/// </remarks>
/// <param name="ProviderName">
/// The provider column stamped into every produced key — the same value the registered
/// <see cref="FactorSeriesDatasetKeyBuilder"/> uses, so append keys and read-scan bounds agree.
/// </param>
public sealed record FactorObservationFanOut(string ProviderName)
    : IDatasetKeyFanOut<FactorSeriesRequest, FactorObservationDatasetKey, FactorObservation, FactorObservation>
{
    /// <inheritdoc />
    public (FactorObservationDatasetKey Key, FactorObservation Record) Project(
        FactorSeriesRequest request, FactorObservation row)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The discriminant is canonicalised HERE, unconditionally — not inherited from whatever
        // FactorObservationNormalizer may or may not have done to the body. Deriving the key from
        // the un-canonicalised body column made the persisted key depend on the
        // RegisterDefaultNormalizers flag, so flipping it over a populated store forked one factor
        // into a raw-era key and a normalized-era key.
        return (
            new FactorObservationDatasetKey(
                ProviderName,
                request.DatasetName.Value,
                request.Frequency,
                FactorObservationDatasetKey.CanonicalFactorName(row.FactorName)),
            row);
    }

    /// <inheritdoc />
    public (FactorObservationDatasetKey Lo, FactorObservationDatasetKey Hi) PrefixBounds(
        FactorSeriesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (
            new FactorObservationDatasetKey(
                ProviderName, request.DatasetName.Value, request.Frequency, string.Empty),
            new FactorObservationDatasetKey(
                ProviderName, request.DatasetName.Value + "\uFFFF", request.Frequency, string.Empty));
    }

    /// <inheritdoc />
    /// <exception cref="Abstractions.Exceptions.MarketDataStorageException">
    /// <paramref name="key"/> belongs to this request's factor set but its <c>FactorName</c>
    /// discriminant is in the pre-canonicalisation encoding — the store directory was populated by a
    /// build that projected the raw body column. Merging it would double-count every date the legacy
    /// and canonical keys share, so it is reported instead of merged; see
    /// <see href="docs/time-series-cache.md#migrating-a-store-written-before-the-canonicalisation">Migrating
    /// a store written before the canonicalisation</see> for the remedy.
    /// </exception>
    public bool KeyMatchesRequest(FactorSeriesRequest request, FactorObservationDatasetKey key)
    {
        ArgumentNullException.ThrowIfNull(request);
        var matches = string.Equals(key.Provider, ProviderName, StringComparison.Ordinal)
            && string.Equals(key.DatasetId, request.DatasetName.Value, StringComparison.Ordinal)
            && key.Frequency == request.Frequency;

        if (!matches)
        {
            return false;
        }

        // Only keys this request would actually merge are checked: a stored key still under the raw
        // spelling was written before the canonicalisation and is NOT the same key this build now
        // projects for that series, so merging it forks the series instead of continuing it.
        LegacyDiscriminantEncodingGuard.EnsureCanonicalDiscriminant(
            nameof(FactorObservation),
            nameof(FactorObservationDatasetKey.FactorName),
            key.FactorName,
            FactorObservationDatasetKey.CanonicalFactorName(key.FactorName));

        return true;
    }
}
