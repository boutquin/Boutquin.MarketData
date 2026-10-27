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

using System.Collections.Concurrent;

using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Default in-memory implementation of <see cref="IMutableTimeSeriesStoreRegistry"/>
/// backed by a <see cref="ConcurrentDictionary{TKey, TValue}"/> keyed by the
/// <c>(TDatasetKey, TRecord)</c> type pair.
/// </summary>
/// <remarks>
/// <para><b>Type-erased storage:</b> the dictionary holds <see cref="object"/> references
/// to the generic <see cref="ITimeSeriesStore{TDatasetKey, TRecord}"/> instances; <see cref="Resolve{TDatasetKey, TRecord}"/>
/// casts on retrieval. Generic constraints are enforced at every public method entry, so the
/// stored object's runtime type always matches the cast target.</para>
/// <para><b>Thread safety:</b> <see cref="Register{TDatasetKey, TRecord}"/> and
/// <see cref="Resolve{TDatasetKey, TRecord}"/> are safe for concurrent use via
/// <see cref="ConcurrentDictionary{TKey, TValue}"/>.</para>
/// <para><b>Lifetime:</b> the registry does not own the registered stores — the caller
/// is responsible for their construction, disposal, and any coverage-index wiring.</para>
/// </remarks>
public sealed class DefaultTimeSeriesStoreRegistry : IMutableTimeSeriesStoreRegistry
{
    private readonly ConcurrentDictionary<(Type Key, Type Record), object> _stores = new();
    private readonly ConcurrentDictionary<(Type Key, Type Record), object> _engines = new();
    private readonly ConcurrentDictionary<(Type Key, Type Record), object> _scanEngines = new();

    /// <inheritdoc />
    public void Register<TDatasetKey, TRecord>(ITimeSeriesStore<TDatasetKey, TRecord> store)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new()
    {
        ArgumentNullException.ThrowIfNull(store);

        _stores[(typeof(TDatasetKey), typeof(TRecord))] = store;
    }

    /// <inheritdoc />
    public ITimeSeriesStore<TDatasetKey, TRecord>? Resolve<TDatasetKey, TRecord>()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new()
    {
        return _stores.TryGetValue((typeof(TDatasetKey), typeof(TRecord)), out var obj)
            ? (ITimeSeriesStore<TDatasetKey, TRecord>)obj
            : null;
    }

    /// <inheritdoc />
    public void RegisterEngine<TDatasetKey, TRecord>(ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> engine)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
    {
        ArgumentNullException.ThrowIfNull(engine);

        _engines[(typeof(TDatasetKey), typeof(TRecord))] = engine;
    }

    /// <inheritdoc />
    public ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> ResolveEngine<TDatasetKey, TRecord>()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
    {
        return _engines.TryGetValue((typeof(TDatasetKey), typeof(TRecord)), out var obj)
            ? (ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>)obj
            : throw new InvalidOperationException(
                $"No time-series storage engine is registered for record type '{typeof(TRecord).FullName}' "
                + $"(dataset key '{typeof(TDatasetKey).FullName}'). Register one via "
                + "IMutableTimeSeriesStoreRegistry.RegisterEngine during DI setup before acquiring a "
                + "read snapshot for this record type.");
    }

    /// <inheritdoc />
    public void RegisterScanEngine<TDatasetKey, TRecord>(ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> engine)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new()
    {
        ArgumentNullException.ThrowIfNull(engine);

        _scanEngines[(typeof(TDatasetKey), typeof(TRecord))] = engine;
    }

    /// <inheritdoc />
    public ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>? ResolveScanEngine<TDatasetKey, TRecord>()
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new()
    {
        return _scanEngines.TryGetValue((typeof(TDatasetKey), typeof(TRecord)), out var obj)
            ? (ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord>)obj
            : null;
    }
}
