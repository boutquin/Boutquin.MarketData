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

using System.Reflection;
using System.Runtime.CompilerServices;
using Boutquin.MarketData.Abstractions.Enums;
using Boutquin.MarketData.Abstractions.Observability;
using Boutquin.Storage.Domain.Enums;
using Boutquin.Storage.Domain.Exceptions;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries.Columnar;
using Boutquin.Storage.Infrastructure.TimeSeries.Columnar;

namespace Boutquin.MarketData.DependencyInjection;

/// <summary>
/// Authenticates each compressed row group and applies the configured consumer policy before
/// records can leave the storage layer.
/// </summary>
internal sealed class QuarantineAwareColumnarSegmentFormat<TRecord> : IColumnarSegmentFormat<TRecord>
    where TRecord : IHasDate, IColumnarRehydratable<TRecord>, new()
{
    private readonly CompressedColumnarSegmentFormat<TRecord> _inner;
    private readonly QuarantineBehavior _behavior;
    private readonly IDataQualityIncidentSink? _incidentSink;
    private readonly QuarantineTracker _tracker;

    public QuarantineAwareColumnarSegmentFormat(
        CompressedColumnarSegmentFormat<TRecord> inner,
        QuarantineBehavior behavior,
        IDataQualityIncidentSink? incidentSink,
        QuarantineTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(tracker);
        if (!Enum.IsDefined(behavior))
        {
            throw new ArgumentOutOfRangeException(nameof(behavior), behavior, "Unsupported quarantine behavior.");
        }
        _inner = inner;
        _behavior = behavior;
        _incidentSink = incidentSink;
        _tracker = tracker;
    }

    public Task WriteAsync(
        Stream output,
        IReadOnlyList<TRecord> records,
        CancellationToken cancellationToken = default)
        => _inner.WriteAsync(output, records, cancellationToken);

    public async IAsyncEnumerable<TRecord> ReadAsync(
        Stream input,
        DateOnly from,
        DateOnly to,
        ColumnProjection projection,
        ScanDirection direction = ScanDirection.Ascending,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in _inner
            .ReadRowGroupsAsync(input, from, to, direction, cancellationToken)
            .ConfigureAwait(false))
        {
            if (!result.IsOk)
            {
                HandleQuarantine(input, result.RowGroupIndex, result.ExpectedCrc, result.ActualCrc, from, to);
                continue;
            }

            foreach (var row in result.Rows!)
            {
                yield return projection.FieldNames.Count > 0 && row is IProjectable<TRecord> projectable
                    ? projectable.ProjectTo(projection)
                    : row;
            }
        }
    }

    public async IAsyncEnumerable<ColumnBatch<TRecord>> ReadBatchesAsync(
        Stream input,
        DateOnly from,
        DateOnly to,
        ColumnProjection projection,
        ScanDirection direction = ScanDirection.Ascending,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in _inner.ReadRowGroupsAsync(input, from, to, direction, cancellationToken).ConfigureAwait(false))
        {
            if (!result.IsOk)
            {
                HandleQuarantine(input, result.RowGroupIndex, result.ExpectedCrc, result.ActualCrc, from, to);
                continue;
            }

            yield return ToBatch(result.Rows!, projection);
        }
    }

    public async IAsyncEnumerable<TRecord> StreamWhereAsync(
        Stream input,
        DateOnly from,
        DateOnly to,
        ColumnProjection projection,
        IReadOnlyList<ColumnPredicate> predicates,
        ScanDirection direction = ScanDirection.Ascending,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicates);
        await foreach (var result in _inner.ReadRowGroupsAsync(input, from, to, direction, cancellationToken).ConfigureAwait(false))
        {
            if (!result.IsOk)
            {
                HandleQuarantine(input, result.RowGroupIndex, result.ExpectedCrc, result.ActualCrc, from, to);
                continue;
            }

            foreach (var row in result.Rows!)
            {
                if (predicates.All(predicate => predicate.Matches(GetPropertyValue(row, predicate.ColumnName))))
                {
                    yield return Project(row, projection);
                }
            }
        }
    }

    private static ColumnBatch<TRecord> ToBatch(IReadOnlyList<TRecord> rows, ColumnProjection projection)
    {
        var properties = Properties(projection);
        var columns = properties.ToDictionary(
            property => property.Name,
            property => ToArray(property, rows),
            StringComparer.Ordinal);
        return new ColumnBatch<TRecord>(rows.Count, columns, rows.Min(row => row.Date), rows.Max(row => row.Date));
    }

    private static TRecord Project(TRecord row, ColumnProjection projection)
        => projection.FieldNames.Count > 0 && row is IProjectable<TRecord> projectable
            ? projectable.ProjectTo(projection)
            : row;

    private static object? GetPropertyValue(TRecord row, string name)
        => typeof(TRecord).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(row);

    private static IEnumerable<PropertyInfo> Properties(ColumnProjection projection)
        => typeof(TRecord).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => projection.FieldNames.Count == 0 || projection.FieldNames.Contains(property.Name, StringComparer.Ordinal));

    private static Array ToArray(PropertyInfo property, IReadOnlyList<TRecord> rows)
    {
        var values = Array.CreateInstance(property.PropertyType, rows.Count);
        for (var index = 0; index < rows.Count; index++)
        {
            values.SetValue(property.GetValue(rows[index]), index);
        }

        return values;
    }

    private void HandleQuarantine(
        Stream input,
        int rowGroupIndex,
        uint expectedCrc,
        uint actualCrc,
        DateOnly from,
        DateOnly to)
    {
        var detail = $"Columnar row group {rowGroupIndex} failed CRC32C authentication " +
            $"(expected 0x{expectedCrc:x8}, actual 0x{actualCrc:x8}).";
        var range = new DateRange(from, to);
        var firstObservation = _tracker.TryRecord(
            rowGroupIndex,
            expectedCrc,
            actualCrc,
            range,
            recover: _behavior == QuarantineBehavior.TreatAsGap,
            segmentIdentity: SegmentIdentity(input));
        if (firstObservation)
        {
            _incidentSink?.Report(new DataQualityIncident(
                DataQualityIncidentKind.RowGroupQuarantine,
                typeof(TRecord).Name,
                DatasetKey: null,
                DateTimeOffset.UtcNow,
                detail));
        }

        if (_behavior == QuarantineBehavior.FailQuery)
        {
            throw new CorruptSegmentException(detail, rowGroupIndex);
        }

        // Storage exposes the quarantined group index and CRCs, but not its date descriptor.
        // TreatAsGap records the caller's bounded window through the tracker above: conservative
        // over-fetch is safer than allowing unauthenticated bytes or guessing a narrower span.
    }

    // Storage attaches this identity to its per-block window.  The wrapper intentionally does
    // not inspect concrete streams: an untagged Stream has no portable physical-file identity
    // and is therefore deduplicated only for the lifetime of that object.
    private static string SegmentIdentity(Stream input) => input is IPhysicalSegmentIdentity physical
        ? physical.PhysicalSegmentIdentity
        : $"stream:{RuntimeHelpers.GetHashCode(input):x}";
}
