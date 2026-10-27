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

using System.Runtime.ExceptionServices;

using Boutquin.Storage.Domain.Interfaces;

namespace Boutquin.MarketData.DependencyInjection;

/// <summary>
/// Delegates an LSM engine while owning the write-ahead log that MarketData allocated for it.
/// Storage treats an injected WAL as caller-owned; this private composition boundary keeps that
/// contract while presenting one disposable engine identity to the store, registry, and callers.
/// </summary>
internal sealed class WalOwningLsmStorageEngine<TKey, TValue> :
    ILsmStorageEngine<TKey, TValue>, IDisposable, IAsyncDisposable
    where TKey : IStorageKey<TKey>, new()
    where TValue : ISerializable<TValue>, new()
{
    private readonly ILsmStorageEngine<TKey, TValue> _inner;
    private readonly IDisposable _wal;
    private int _disposeGate;

    internal WalOwningLsmStorageEngine(
        ILsmStorageEngine<TKey, TValue> inner,
        IDisposable wal)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(wal);
        _inner = inner;
        _wal = wal;
    }

    public bool SupportsSnapshots => _inner.SupportsSnapshots;

    public int SegmentCount => _inner.SegmentCount;

    public Task SetAsync(TKey key, TValue value, CancellationToken cancellationToken = default)
        => _inner.SetAsync(key, value, cancellationToken);

    public Task<(TValue Value, bool Found)> TryGetValueAsync(
        TKey key,
        CancellationToken cancellationToken = default)
        => _inner.TryGetValueAsync(key, cancellationToken);

    public Task<bool> ContainsKeyAsync(TKey key, CancellationToken cancellationToken = default)
        => _inner.ContainsKeyAsync(key, cancellationToken);

    public Task RemoveAsync(TKey key, CancellationToken cancellationToken = default)
        => _inner.RemoveAsync(key, cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default)
        => _inner.ClearAsync(cancellationToken);

    public Task<IEnumerable<(TKey Key, TValue Value)>> GetAllItemsAsync(
        CancellationToken cancellationToken = default)
        => _inner.GetAllItemsAsync(cancellationToken);

    public Task SetBulkAsync(
        IEnumerable<KeyValuePair<TKey, TValue>> items,
        CancellationToken cancellationToken = default)
        => _inner.SetBulkAsync(items, cancellationToken);

    public Task FlushAsync(CancellationToken cancellationToken = default)
        => _inner.FlushAsync(cancellationToken);

    public Task CompactAsync(CancellationToken cancellationToken = default)
        => _inner.CompactAsync(cancellationToken);

    public IAsyncEnumerable<(TKey Key, TValue Value)> GetRangeAsync(
        TKey startKey,
        TKey endKey,
        CancellationToken cancellationToken = default)
        => _inner.GetRangeAsync(startKey, endKey, cancellationToken);

    public Task<ISnapshot<TKey, TValue>> AcquireSnapshotAsync(
        CancellationToken cancellationToken = default)
        => _inner.AcquireSnapshotAsync(cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeGate, 1) != 0)
        {
            return;
        }

        Exception? engineFailure = null;
        try
        {
            (_inner as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            engineFailure = ex;
        }

        try
        {
            _wal.Dispose();
        }
        catch (Exception cleanupFailure) when (engineFailure is not null)
        {
            RecordCleanupFailure(engineFailure, cleanupFailure);
        }

        if (engineFailure is not null)
        {
            ExceptionDispatchInfo.Capture(engineFailure).Throw();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeGate, 1) != 0)
        {
            return;
        }

        Exception? engineFailure = null;
        try
        {
            if (_inner is IAsyncDisposable asyncInner)
            {
                await asyncInner.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                (_inner as IDisposable)?.Dispose();
            }
        }
        catch (Exception ex)
        {
            engineFailure = ex;
        }

        try
        {
            _wal.Dispose();
        }
        catch (Exception cleanupFailure) when (engineFailure is not null)
        {
            RecordCleanupFailure(engineFailure, cleanupFailure);
        }

        if (engineFailure is not null)
        {
            ExceptionDispatchInfo.Capture(engineFailure).Throw();
        }
    }

    internal static void DisposeAfterBuildFailure(
        WalOwningLsmStorageEngine<TKey, TValue>? owner,
        IDisposable wal,
        Exception originalFailure)
    {
        try
        {
            if (owner is not null)
            {
                owner.Dispose();
            }
            else
            {
                wal.Dispose();
            }
        }
        catch (Exception cleanupFailure)
        {
            RecordCleanupFailure(originalFailure, cleanupFailure);
        }
    }

    private static void RecordCleanupFailure(Exception originalFailure, Exception cleanupFailure)
    {
        try
        {
            originalFailure.Data["MarketData.WalCleanupFailure"] = cleanupFailure;
        }
        catch (Exception)
        {
            // Diagnostic attachment is strictly best-effort. Exception.Data is virtual and may expose
            // an immutable or throwing dictionary; no diagnostic failure may replace the original error.
        }
    }
}
