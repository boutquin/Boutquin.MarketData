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

using Boutquin.MarketData.Cache.TimeSeries;

using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;

namespace Boutquin.MarketData.DependencyInjection;

/// <summary>
/// A pending time-series store registration captured by <c>AddTimeSeriesStore</c> and applied to
/// the <see cref="IMutableTimeSeriesStoreRegistry"/> when the registry singleton is first
/// resolved. Type-erases the generic store so a heterogeneous set of stores can be enumerated as
/// one DI service collection. Extends <see cref="IAsyncDisposable"/>/<see cref="IDisposable"/> so
/// that, when registered via a container-owned factory, disposing the <c>ServiceProvider</c>
/// releases the underlying LSM engine's WAL/file handles.
/// </summary>
public interface ITimeSeriesStoreRegistration : IDisposable, IAsyncDisposable
{
    /// <summary>Applies this registration's store to <paramref name="registry"/>.</summary>
    void ApplyTo(IMutableTimeSeriesStoreRegistry registry);
}

/// <summary>
/// Concrete <see cref="ITimeSeriesStoreRegistration"/> carrying a constructed
/// <see cref="ITimeSeriesStore{TDatasetKey, TRecord}"/> together with the inner
/// <see cref="ILsmStorageEngine{TKey, TValue}"/> that backs it, re-applying both generic
/// registrations at <see cref="ApplyTo"/> time. Registering the engine alongside the store is
/// what makes <see cref="ITimeSeriesStoreRegistry.ResolveEngine{TDatasetKey, TRecord}"/> live for
/// the snapshot-isolated read path.
/// </summary>
/// <typeparam name="TDatasetKey">Dataset partition key type.</typeparam>
/// <typeparam name="TRecord">Dated, columnar-rehydratable record type.</typeparam>
public sealed class TimeSeriesStoreRegistration<TDatasetKey, TRecord> : ITimeSeriesStoreRegistration
    where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
{
    private readonly ITimeSeriesStore<TDatasetKey, TRecord> _store;
    private readonly ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> _engine;
    private int _disposeGate;

    /// <summary>Creates a registration for <paramref name="store"/> and its backing <paramref name="engine"/>.</summary>
    public TimeSeriesStoreRegistration(
        ITimeSeriesStore<TDatasetKey, TRecord> store,
        ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> engine)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(engine);
        _store = store;
        _engine = engine;
    }

    /// <inheritdoc />
    public void ApplyTo(IMutableTimeSeriesStoreRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeGate) != 0, this);
        registry.Register(_store);
        registry.RegisterEngine(_engine);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeGate, 1) != 0)
        {
            return;
        }

        (_engine as IDisposable)?.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeGate, 1) != 0)
        {
            return;
        }

        if (_engine is IAsyncDisposable asyncEngine)
        {
            await asyncEngine.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            (_engine as IDisposable)?.Dispose();
        }
    }
}

/// <summary>
/// Registration for a <b>row-format</b> store and its owned engine. All record types receive
/// store and row-scan registrations. Records implementing <c>IColumnarRehydratable</c> also
/// receive the constrained engine registration used by existing scan and snapshot bindings,
/// including when a row store replaces a kernel columnar default. A
/// <c>[StorageSerializable]</c>-only record retains its row-scan capability without acquiring
/// the unsupported snapshot-read capability.
/// </summary>
/// <typeparam name="TDatasetKey">Dataset partition key type.</typeparam>
/// <typeparam name="TRecord">Dated, row-format (<c>[StorageSerializable]</c>-only) record type.</typeparam>
public sealed class RowTimeSeriesStoreRegistration<TDatasetKey, TRecord> : ITimeSeriesStoreRegistration
    where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    where TRecord : ISerializable<TRecord>, IHasDate, new()
{
    private readonly ITimeSeriesStore<TDatasetKey, TRecord> _store;
    private readonly ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> _engine;
    private int _disposeGate;

    /// <summary>
    /// Creates a registration for <paramref name="store"/> and captures its backing
    /// <paramref name="engine"/> for disposal and the read surfaces supported by the record type.
    /// </summary>
    public RowTimeSeriesStoreRegistration(
        ITimeSeriesStore<TDatasetKey, TRecord> store,
        ILsmStorageEngine<TimeSeriesKey<TDatasetKey>, TRecord> engine)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(engine);
        _store = store;
        _engine = engine;
    }

    /// <inheritdoc />
    public void ApplyTo(IMutableTimeSeriesStoreRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeGate) != 0, this);
        registry.Register(_store);

        // Register every inner engine for the row-safe cross-key SCAN surface. This backs the per-row
        // fan-out read merge for a row-format fan-out record (OptionQuote, the keyed-row envelope's stored
        // type): RowLsmCrossKeyScan resolves this engine via ResolveScanEngine to issue a composite-key
        // range scan. Harmless for non-fan-out row records (FuturesSettlement, DividendPayment) — the
        // scan is only consulted when a matching IDatasetKeyFanOut is registered.
        registry.RegisterScanEngine(_engine);

        // Segment format does not remove the record type's snapshot/rehydration capability.
        // The runtime check lets this row-safe generic registration preserve the constrained
        // engine binding without imposing IColumnarRehydratable on genuinely row-only records.
        if (typeof(TRecord).GetInterfaces().Any(static type => type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(IColumnarRehydratable<>)
            && type.GenericTypeArguments[0] == typeof(TRecord)))
        {
            typeof(IMutableTimeSeriesStoreRegistry)
                .GetMethod(nameof(IMutableTimeSeriesStoreRegistry.RegisterEngine))!
                .MakeGenericMethod(typeof(TDatasetKey), typeof(TRecord))
                .Invoke(registry, [_engine]);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeGate, 1) != 0)
        {
            return;
        }

        (_engine as IDisposable)?.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeGate, 1) != 0)
        {
            return;
        }

        if (_engine is IAsyncDisposable asyncEngine)
        {
            await asyncEngine.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            (_engine as IDisposable)?.Dispose();
        }
    }
}
