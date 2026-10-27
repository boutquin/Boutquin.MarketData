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

using Boutquin.MarketData.Orchestration.Pipeline;

using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries.Columnar;

namespace Boutquin.MarketData.DependencyInjection;

internal sealed class QuarantineTrackingTimeSeriesStore<TDatasetKey, TRecord> :
    ITimeSeriesStore<TDatasetKey, TRecord>,
    IQuarantineRecoverySource
    where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
    where TRecord : ISerializable<TRecord>, IHasDate, new()
{
    private readonly ITimeSeriesStore<TDatasetKey, TRecord> _inner;
    private readonly QuarantineTracker _tracker;

    public QuarantineTrackingTimeSeriesStore(
        ITimeSeriesStore<TDatasetKey, TRecord> inner,
        QuarantineTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(tracker);
        _inner = inner;
        _tracker = tracker;
    }

    public Task AppendAsync(TDatasetKey dataset, IEnumerable<TRecord> records, CancellationToken cancellationToken = default)
        => _inner.AppendAsync(dataset, records, cancellationToken);

    public IAsyncEnumerable<TRecord> GetRangeAsync(
        TDatasetKey dataset,
        DateOnly from,
        DateOnly to,
        ColumnProjection? projection = null,
        CancellationToken cancellationToken = default)
        => _inner.GetRangeAsync(dataset, from, to, projection, cancellationToken);

    public Task<DateRange?> GetCoverageAsync(TDatasetKey dataset, CancellationToken cancellationToken = default)
        => _inner.GetCoverageAsync(dataset, cancellationToken);

    public Task<IReadOnlyList<DateRange>> GetGapsAsync(
        TDatasetKey dataset,
        DateRange requested,
        CancellationToken cancellationToken = default)
        => _inner.GetGapsAsync(dataset, requested, cancellationToken);

    public IAsyncEnumerable<TRecord> GetTailAsync(
        TDatasetKey dataset,
        int count,
        DateOnly? onOrBefore = null,
        ColumnProjection? projection = null,
        CancellationToken cancellationToken = default)
        => _inner.GetTailAsync(dataset, count, onOrBefore, projection, cancellationToken);

    public Task DeleteRangeAsync(
        TDatasetKey dataset,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
        => _inner.DeleteRangeAsync(dataset, from, to, cancellationToken);

    public IReadOnlyList<DateRange> DrainQuarantinedRanges() => _tracker.Drain();
}
