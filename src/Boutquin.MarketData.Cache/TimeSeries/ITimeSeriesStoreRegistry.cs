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

using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Factory that resolves the <see cref="ITimeSeriesStore{TDatasetKey, TRecord}"/> for a given
/// (dataset-key, record) type pair. Registered as a DI singleton; one store instance per
/// record type, shared across all <c>DataPipeline</c> calls.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> decouple <c>DataPipeline</c> from the specific construction recipe
/// for each record-type's time-series store. The pipeline asks the registry for the store
/// matching the request's record type; the registry returns the pre-constructed instance.</para>
/// <para><b>Registration:</b> stores are registered via
/// <see cref="IMutableTimeSeriesStoreRegistry.Register{TDatasetKey, TRecord}"/> during DI
/// container setup (see <c>AddBoutquinMarketData</c> extension). The read-only
/// <see cref="ITimeSeriesStoreRegistry"/> interface is what <c>DataPipeline</c> injects.</para>
/// <para><b>Generic constraints:</b> match <see cref="ITimeSeriesStore{TDatasetKey,TRecord}"/>
/// — keys must be serializable + comparable + default-constructable; records must be
/// serializable + dated + default-constructable. Callers are responsible for ensuring the
/// type pair has been registered; see <see cref="Resolve{TDatasetKey, TRecord}"/> for the
/// not-registered behavior.</para>
/// </remarks>
public interface ITimeSeriesStoreRegistry
{
    /// <summary>
    /// Returns the registered <see cref="ITimeSeriesStore{TDatasetKey, TRecord}"/> for the
    /// specified type pair, or <see langword="null"/> if no store has been registered.
    /// </summary>
    /// <typeparam name="TDatasetKey">The dataset partition key type.</typeparam>
    /// <typeparam name="TRecord">The dated record type.</typeparam>
    ITimeSeriesStore<TDatasetKey, TRecord>? Resolve<TDatasetKey, TRecord>()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new();

    /// <summary>
    /// Resolves the underlying <see cref="ILsmStorageEngine{TKey, TValue}"/> for a record type so
    /// that consumers of the snapshot-isolated read surface can call
    /// <see cref="ILsmStorageEngine{TKey, TValue}.AcquireSnapshotAsync"/> to pin a point-in-time
    /// read view. Exposed only to the pipeline's snapshot path; adapter authors never consume it
    /// directly.
    /// </summary>
    /// <typeparam name="TDatasetKey">The dataset partition key type.</typeparam>
    /// <typeparam name="TRecord">The dated, columnar-rehydratable record type.</typeparam>
    /// <returns>The registered engine for the <c>(TDatasetKey, TRecord)</c> pair.</returns>
    /// <exception cref="InvalidOperationException">
    /// No engine has been registered for <typeparamref name="TRecord"/>. The message names the
    /// unregistered record type. This method never returns <see langword="null"/> or a sentinel —
    /// a missing registration is a composition error, surfaced eagerly.
    /// </exception>
    /// <remarks>
    /// The <see cref="IColumnarRehydratable{TSelf}"/> constraint matches the
    /// <c>LsmTimeSeriesStore</c> stack requirement; the columnar source generator emits it
    /// for every <c>[ColumnarStorable]</c> record, so the constraint costs nothing at the call
    /// site while letting the registry surface a strongly-typed engine.
    /// </remarks>
    ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> ResolveEngine<TDatasetKey, TRecord>()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new();

    /// <summary>
    /// Resolves the underlying <see cref="ILsmStorageEngine{TKey, TValue}"/> for a record type for a
    /// <b>cross-dataset range scan</b> (the per-row-discriminant fan-out read merge), or
    /// <see langword="null"/> when no scan engine has been registered for the pair. Unlike
    /// <see cref="ResolveEngine{TDatasetKey, TRecord}"/> this does <b>not</b> constrain
    /// <typeparamref name="TRecord"/> to <see cref="IColumnarRehydratable{TSelf}"/>: a
    /// <b>row-format</b> fan-out record (<c>OptionQuote</c>, the keyed-row envelope's stored type) has no
    /// columnar engine, yet its inner engine still answers a composite-key range scan. The scan is
    /// read-only over the composite <c>(dataset, date)</c> key space; it does not pin a snapshot, so it
    /// needs none of the columnar-snapshot machinery <see cref="ResolveEngine{TDatasetKey, TRecord}"/>
    /// exists for.
    /// </summary>
    /// <typeparam name="TDatasetKey">The dataset partition key type.</typeparam>
    /// <typeparam name="TRecord">The dated record type (columnar or row).</typeparam>
    /// <returns>The registered scan engine for the pair, or <see langword="null"/> if none.</returns>
    ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>? ResolveScanEngine<TDatasetKey, TRecord>()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new();
}

/// <summary>
/// Mutable view of <see cref="ITimeSeriesStoreRegistry"/> used during DI setup to register
/// one store per record type. Not intended for runtime use — <c>DataPipeline</c> consumes
/// the read-only <see cref="ITimeSeriesStoreRegistry"/>.
/// </summary>
public interface IMutableTimeSeriesStoreRegistry : ITimeSeriesStoreRegistry
{
    /// <summary>
    /// Registers a <see cref="ITimeSeriesStore{TDatasetKey, TRecord}"/> instance for the
    /// specified type pair. Re-registration replaces the prior entry — last writer wins.
    /// </summary>
    void Register<TDatasetKey, TRecord>(ITimeSeriesStore<TDatasetKey, TRecord> store)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new();

    /// <summary>
    /// Registers the underlying <see cref="ILsmStorageEngine{TKey, TValue}"/> for the specified
    /// type pair so the snapshot path can resolve it via
    /// <see cref="ITimeSeriesStoreRegistry.ResolveEngine{TDatasetKey, TRecord}"/>. Re-registration
    /// replaces the prior entry — last writer wins. A composition-root / startup operation;
    /// single-writer (see the registry remarks).
    /// </summary>
    /// <typeparam name="TDatasetKey">The dataset partition key type.</typeparam>
    /// <typeparam name="TRecord">The dated, columnar-rehydratable record type.</typeparam>
    /// <param name="engine">The engine whose <c>AcquireSnapshotAsync</c> the snapshot path pins.</param>
    void RegisterEngine<TDatasetKey, TRecord>(ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> engine)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new();

    /// <summary>
    /// Registers the underlying <see cref="ILsmStorageEngine{TKey, TValue}"/> for the specified type
    /// pair so the fan-out read merge can resolve it via
    /// <see cref="ITimeSeriesStoreRegistry.ResolveScanEngine{TDatasetKey, TRecord}"/>. Re-registration
    /// replaces the prior entry — last writer wins. Row-safe: unlike
    /// <see cref="RegisterEngine{TDatasetKey, TRecord}"/> it does not require
    /// <see cref="IColumnarRehydratable{TSelf}"/>, so a row-format fan-out record's engine can be
    /// registered for cross-dataset scanning. A composition-root / startup operation; single-writer.
    /// </summary>
    /// <typeparam name="TDatasetKey">The dataset partition key type.</typeparam>
    /// <typeparam name="TRecord">The dated record type (columnar or row).</typeparam>
    /// <param name="engine">The engine whose composite-key range scan backs the fan-out read merge.</param>
    void RegisterScanEngine<TDatasetKey, TRecord>(ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> engine)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new();
}
