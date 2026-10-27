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

using System.Buffers.Binary;

using Boutquin.MarketData.Abstractions.Enums;
using Boutquin.MarketData.Abstractions.Observability;
using Boutquin.MarketData.Abstractions.Records;

using Boutquin.Storage.Domain.Exceptions;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries.Columnar;
using Boutquin.Storage.Infrastructure.TimeSeries.Columnar;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

public sealed class QuarantineAwareColumnarSegmentFormatTests
{
    private static readonly DateOnly s_from = new(2024, 1, 1);
    private static readonly DateOnly s_to = new(2024, 1, 4);

    [Fact]
    public async Task FailQuery_throws_typed_exception_for_a_real_crc_mismatch()
    {
        var (stream, inner) = await CorruptFirstRowGroupAsync().ConfigureAwait(true);
        var sink = new CapturingIncidentSink();
        var format = new QuarantineAwareColumnarSegmentFormat<Bar>(
            inner, QuarantineBehavior.FailQuery, sink, new QuarantineTracker());

        var act = async () => await ReadAllAsync(format, stream).ConfigureAwait(true);

        (await act.Should().ThrowAsync<CorruptSegmentException>().ConfigureAwait(true))
            .Which.Message.Should().Contain("row group 0");
        sink.Incidents.Should().ContainSingle()
            .Which.Kind.Should().Be(DataQualityIncidentKind.RowGroupQuarantine);
    }

    [Fact]
    public async Task SkipRowGroup_returns_only_authenticated_rows_and_reports_incident()
    {
        var (stream, inner) = await CorruptFirstRowGroupAsync().ConfigureAwait(true);
        var sink = new CapturingIncidentSink();
        var tracker = new QuarantineTracker();
        var format = new QuarantineAwareColumnarSegmentFormat<Bar>(
            inner, QuarantineBehavior.SkipRowGroup, sink, tracker);

        var rows = await ReadAllAsync(format, stream).ConfigureAwait(true);

        rows.Select(r => r.Date).Should().Equal(new DateOnly(2024, 1, 3), s_to);
        sink.Incidents.Should().ContainSingle();
        tracker.Drain().Should().BeEmpty("SkipRowGroup does not schedule a recovery fetch");
    }

    [Fact]
    public async Task TreatAsGap_returns_authenticated_rows_and_tracks_recovery_range()
    {
        var (stream, inner) = await CorruptFirstRowGroupAsync().ConfigureAwait(true);
        var sink = new CapturingIncidentSink();
        var tracker = new QuarantineTracker();
        var format = new QuarantineAwareColumnarSegmentFormat<Bar>(
            inner, QuarantineBehavior.TreatAsGap, sink, tracker);

        // Direct formatter reads have no pipeline request to own recovery work; enter the internal
        // request-shaped scope so this unit test can observe the range it deliberately records.
        using var recoveryScope = tracker.EnterRecoveryScope();
        var rows = await ReadAllAsync(format, stream).ConfigureAwait(true);

        rows.Select(r => r.Date).Should().Equal(new DateOnly(2024, 1, 3), s_to);
        tracker.Drain().Should().Equal(new DateRange(s_from, s_to));
        sink.Incidents.Should().ContainSingle();
    }

    [Fact]
    public async Task FailQuery_batch_surface_applies_the_quarantine_policy()
    {
        var (stream, inner) = await CorruptFirstRowGroupAsync().ConfigureAwait(true);
        var format = new QuarantineAwareColumnarSegmentFormat<Bar>(inner, QuarantineBehavior.FailQuery, new CapturingIncidentSink(), new QuarantineTracker());

        var act = async () => { await foreach (var _ in format.ReadBatchesAsync(stream, s_from, s_to, ColumnProjection.All)) { } };

        await act.Should().ThrowAsync<CorruptSegmentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task FailQuery_predicate_surface_applies_the_quarantine_policy()
    {
        var (stream, inner) = await CorruptFirstRowGroupAsync().ConfigureAwait(true);
        var format = new QuarantineAwareColumnarSegmentFormat<Bar>(inner, QuarantineBehavior.FailQuery, new CapturingIncidentSink(), new QuarantineTracker());

        var act = async () => { await foreach (var _ in format.StreamWhereAsync(stream, s_from, s_to, ColumnProjection.All, [])) { } };

        await act.Should().ThrowAsync<CorruptSegmentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task SkipRowGroup_batch_surface_returns_only_authenticated_batches()
    {
        var (stream, inner) = await CorruptFirstRowGroupAsync().ConfigureAwait(true);
        var format = new QuarantineAwareColumnarSegmentFormat<Bar>(inner, QuarantineBehavior.SkipRowGroup, new CapturingIncidentSink(), new QuarantineTracker());
        var batches = new List<ColumnBatch<Bar>>();

        await foreach (var batch in format.ReadBatchesAsync(stream, s_from, s_to, ColumnProjection.All)) { batches.Add(batch); }

        batches.Should().ContainSingle().Which.RowCount.Should().Be(2);
    }

    [Fact]
    public async Task TreatAsGap_predicate_surface_returns_only_authenticated_rows()
    {
        var (stream, inner) = await CorruptFirstRowGroupAsync().ConfigureAwait(true);
        var format = new QuarantineAwareColumnarSegmentFormat<Bar>(inner, QuarantineBehavior.TreatAsGap, new CapturingIncidentSink(), new QuarantineTracker());
        var rows = new List<Bar>();

        await foreach (var row in format.StreamWhereAsync(stream, s_from, s_to, ColumnProjection.All, [])) { rows.Add(row); }

        rows.Select(x => x.Date).Should().Equal(new DateOnly(2024, 1, 3), s_to);
    }

    [Fact]
    public async Task ReopenedFileAcrossRequestWindows_reports_one_physical_incident()
    {
        var (source, inner) = await CorruptFirstRowGroupAsync().ConfigureAwait(true);
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, source.ToArray()).ConfigureAwait(true);
            var sink = new CapturingIncidentSink();
            var format = new QuarantineAwareColumnarSegmentFormat<Bar>(inner, QuarantineBehavior.SkipRowGroup, sink, new QuarantineTracker());
            await using (var first = new IdentityBearingStream(File.OpenRead(path), $"file:{path}")) { await ReadAllAsync(format, first, s_from, s_to).ConfigureAwait(true); }
            await using (var second = new IdentityBearingStream(File.OpenRead(path), $"file:{path}")) { await ReadAllAsync(format, second, new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3)).ConfigureAwait(true); }
            sink.Incidents.Should().ContainSingle();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DistinctFilesWithMatchingDamage_report_distinct_physical_incidents()
    {
        var (source, inner) = await CorruptFirstRowGroupAsync().ConfigureAwait(true);
        var firstPath = Path.GetTempFileName(); var secondPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(firstPath, source.ToArray()).ConfigureAwait(true); await File.WriteAllBytesAsync(secondPath, source.ToArray()).ConfigureAwait(true);
            var sink = new CapturingIncidentSink(); var format = new QuarantineAwareColumnarSegmentFormat<Bar>(inner, QuarantineBehavior.SkipRowGroup, sink, new QuarantineTracker());
            await using (var first = new IdentityBearingStream(File.OpenRead(firstPath), $"file:{firstPath}")) { await ReadAllAsync(format, first, s_from, s_to).ConfigureAwait(true); }
            await using (var second = new IdentityBearingStream(File.OpenRead(secondPath), $"file:{secondPath}")) { await ReadAllAsync(format, second, s_from, s_to).ConfigureAwait(true); }
            sink.Incidents.Should().HaveCount(2);
        }
        finally { File.Delete(firstPath); File.Delete(secondPath); }
    }

    [Fact]
    public async Task UntaggedStreams_are_deduplicated_only_per_stream_object()
    {
        var (first, inner) = await CorruptFirstRowGroupAsync().ConfigureAwait(true);
        var second = new MemoryStream(first.ToArray(), writable: false);
        var sink = new CapturingIncidentSink(); var format = new QuarantineAwareColumnarSegmentFormat<Bar>(inner, QuarantineBehavior.SkipRowGroup, sink, new QuarantineTracker());
        await ReadAllAsync(format, first, s_from, s_to).ConfigureAwait(true); await ReadAllAsync(format, first, new DateOnly(2024, 1, 2), s_to).ConfigureAwait(true); await ReadAllAsync(format, second, s_from, s_to).ConfigureAwait(true);
        sink.Incidents.Should().HaveCount(2);
    }

    private static async Task<(MemoryStream Stream, CompressedColumnarSegmentFormat<Bar> Inner)>
        CorruptFirstRowGroupAsync()
    {
        var inner = new CompressedColumnarSegmentFormat<Bar>(rowGroupMaxRows: 2, TimeSpan.FromDays(365));
        var stream = new MemoryStream();
        await inner.WriteAsync(stream,
        [
            Bar(s_from, 101m),
            Bar(new DateOnly(2024, 1, 2), 102m),
            Bar(new DateOnly(2024, 1, 3), 103m),
            Bar(s_to, 104m),
        ]).ConfigureAwait(true);

        var bytes = stream.ToArray();
        var footerLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 8, 4)));
        var footerStart = bytes.Length - 8 - 4 - footerLength;
        var footer = bytes.AsSpan(footerStart, footerLength);

        // Locate the RowGroupCrcs extension header for two groups:
        // [kind=3][payloadLength=10:uint32][count=2:uint16][crc:uint32].... Flip the first
        // recorded CRC, then authenticate the modified footer so the reader reaches the row-group
        // check rather than rejecting the footer itself.
        ReadOnlySpan<byte> rowGroupCrcHeader = [0x03, 0x0a, 0x00, 0x00, 0x00, 0x02, 0x00];
        var extensionOffset = footer.IndexOf(rowGroupCrcHeader);
        extensionOffset.Should().BeGreaterThanOrEqualTo(0, "Storage emits row-group CRC metadata");
        var firstRowGroupCrcOffset = extensionOffset + rowGroupCrcHeader.Length;
        footer[firstRowGroupCrcOffset] ^= 0xff;
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(footerStart + footerLength, 4),
            ComputeCrc32C(footer));

        return (new MemoryStream(bytes, writable: false), inner);
    }

    private static async Task<List<Bar>> ReadAllAsync(
        QuarantineAwareColumnarSegmentFormat<Bar> format,
        Stream stream) => await ReadAllAsync(format, stream, s_from, s_to).ConfigureAwait(true);

    private static async Task<List<Bar>> ReadAllAsync(
        QuarantineAwareColumnarSegmentFormat<Bar> format,
        Stream stream,
        DateOnly from,
        DateOnly to)
    {
        var rows = new List<Bar>();
        stream.Position = 0;
        await foreach (var row in format.ReadAsync(stream, from, to, ColumnProjection.All))
        {
            rows.Add(row);
        }

        return rows;
    }

    private static Bar Bar(DateOnly date, decimal close)
        => new(date, close, close, close, close, close, 1_000);

    private static uint ComputeCrc32C(ReadOnlySpan<byte> bytes)
    {
        const uint reflectedCastagnoliPolynomial = 0x82f63b78u;
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : reflectedCastagnoliPolynomial);
            }
        }

        return ~crc;
    }

    private sealed class IdentityBearingStream(Stream inner, string physicalSegmentIdentity)
        : Stream, IPhysicalSegmentIdentity
    {
        public string PhysicalSegmentIdentity { get; } = string.IsNullOrWhiteSpace(physicalSegmentIdentity)
            ? throw new ArgumentException("A physical segment identity is required.", nameof(physicalSegmentIdentity))
            : physicalSegmentIdentity;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => inner.CanWrite;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class CapturingIncidentSink : IDataQualityIncidentSink
    {
        public List<DataQualityIncident> Incidents { get; } = [];

        public void Report(DataQualityIncident incident) => Incidents.Add(incident);
    }
}
