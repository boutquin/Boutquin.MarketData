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

using Boutquin.MarketData.Abstractions.Primitives;
using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions;

/// <summary>
/// Read-only store of published rate-benchmark fixings, keyed by
/// <see cref="BenchmarkName"/> and observation date.
/// </summary>
/// <remarks>
/// <para>
/// Fixings are the realised values of a benchmark (SOFR = 5.33% on
/// 2025-10-15, etc.). Any instrument that references past fixings — an
/// already-accruing OIS swap, a floored note, a backward-looking compound
/// rate — needs a store like this to value correctly.
/// </para>
/// <para>
/// The v1 contract is read-only by design: persisting fixings requires a
/// decision about backing-store semantics (append-only vs. idempotent upsert,
/// concurrency model, durability guarantee) that is better scoped to a
/// follow-up spec alongside a concrete storage-backed implementation. The
/// <c>InMemoryFixingsStore</c> in-tree implementation accepts its data via
/// constructor seed.
/// </para>
/// </remarks>
public interface IFixingsStore
{
    /// <summary>
    /// Returns the fixing value for <paramref name="benchmark"/> on
    /// <paramref name="fixingDate"/>, or <see langword="null"/> when no fixing
    /// is present.
    /// </summary>
    /// <param name="benchmark">The benchmark identifier.</param>
    /// <param name="fixingDate">The observation date.</param>
    /// <returns>The published fixing, or <see langword="null"/>.</returns>
    decimal? GetFixing(BenchmarkName benchmark, DateOnly fixingDate);

    /// <summary>
    /// Attempts to retrieve a fixing without allocating a nullable.
    /// </summary>
    /// <param name="benchmark">The benchmark identifier.</param>
    /// <param name="fixingDate">The observation date.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, contains the published
    /// fixing; otherwise, <c>default(decimal)</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a fixing was found; otherwise <see langword="false"/>.
    /// </returns>
    bool TryGetFixing(BenchmarkName benchmark, DateOnly fixingDate, out decimal value);

    /// <summary>
    /// Returns every fixing for <paramref name="benchmark"/> whose date falls
    /// in <paramref name="range"/>, ordered ascending by date.
    /// </summary>
    /// <param name="benchmark">The benchmark identifier.</param>
    /// <param name="range">The inclusive date range to query.</param>
    /// <returns>
    /// A non-null list of <see cref="TimeSeriesPoint{T}"/> — empty when no
    /// fixings exist in the range, but never <see langword="null"/>.
    /// </returns>
    IReadOnlyList<TimeSeriesPoint<decimal>> GetFixings(BenchmarkName benchmark, DateRange range);
}
