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
using Boutquin.MarketData.Storage.Events;
using Boutquin.Storage.Infrastructure.Events;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit.Events;

/// <summary>
/// Regression tests for the writer/reader frame disagreement observed when more than one
/// kernel instance appends to the same ingestion log file within a single process lifetime.
///
/// <para>Field incident (2026-07-14/16, etfwealthiq multi-blueprint research run): two
/// per-provider <c>AppendOnlyEventLog</c> singletons appended to one <c>ingestion.log</c>.
/// Each writer stream tracked its file offset independently, so the second writer's frame
/// landed at a stale offset — burying the first writer's committed frame and shearing the
/// leading magic bytes off the next one. Every subsequent reader in the same process then
/// failed with <see cref="InvalidDataException"/>: "Event log frame at byte offset N is
/// corrupt but valid frames follow it".</para>
/// </summary>
public sealed class IngestionEventLogFileConcurrencyTests : IDisposable
{
    private readonly DirectoryInfo _dir =
        Directory.CreateTempSubdirectory("marketdata-eventlog-tests-");

    private string LogPath => Path.Combine(_dir.FullName, "ingestion.log");

    public void Dispose() => _dir.Delete(recursive: true);

    private static IngestionEvent MakeEvent(string symbol) =>
        new("tiingo", "daily", symbol, IngestionAction.Fetched, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Interleaved_appends_from_two_instances_preserve_every_event_and_stay_readable()
    {
        // Arrange — writer A is the first pipeline's singleton; it pins its stream offset
        // by appending twice.
        var writerA = await AppendOnlyEventLog<IngestionEvent>.CreateAsync(LogPath)
            .ConfigureAwait(true);
        await writerA.AppendAsync(MakeEvent("AAPL")).ConfigureAwait(true); // position 0
        await writerA.AppendAsync(MakeEvent("MSFT")).ConfigureAwait(true); // position 1

        // Writer B is a second kernel instance over the same file — the multi-provider
        // process shape (one ServiceProvider per research blueprint).
        var writerB = await AppendOnlyEventLog<IngestionEvent>.CreateAsync(LogPath)
            .ConfigureAwait(true);

        // Act — interleave: B appends a LONGER frame at the live end, then A appends
        // SHORTER frames again from its own (now stale) tracked offset.
        var posB = await writerB
            .AppendAsync(MakeEvent("VGSH-FROM-SECOND-WRITER-INSTANCE"))
            .ConfigureAwait(true);
        var posA1 = await writerA.AppendAsync(MakeEvent("TLT")).ConfigureAwait(true);
        var posA2 = await writerA.AppendAsync(MakeEvent("VGLT")).ConfigureAwait(true);

        // Assert — a same-process reader replaying from 0 must see every acknowledged
        // event exactly once, at gap-free monotonically increasing positions, and must
        // not throw. (The incident build threw InvalidDataException here: writer A's
        // stale-offset frame sheared writer B's committed frame mid-log.)
        var replayed = new List<(long Position, IngestionEvent Event)>();
        await foreach (var item in writerA.ReadFromAsync(0).ConfigureAwait(true))
        {
            replayed.Add(item);
        }

        replayed.Select(x => x.Position).Should().Equal(0L, 1L, 2L, 3L, 4L);
        replayed.Select(x => x.Event.Symbol).Should().BeEquivalentTo(
            ["AAPL", "MSFT", "VGSH-FROM-SECOND-WRITER-INSTANCE", "TLT", "VGLT"]);
        new[] { posB, posA1, posA2 }.Should().OnlyHaveUniqueItems(
            "two cooperating writers must never acknowledge the same position twice");

        writerA.Dispose();
        writerB.Dispose();
    }

    [Fact]
    public async Task Fresh_reader_instance_recovers_cleanly_after_interleaved_appends()
    {
        // Arrange — same interleave as above.
        var writerA = await AppendOnlyEventLog<IngestionEvent>.CreateAsync(LogPath)
            .ConfigureAwait(true);
        await writerA.AppendAsync(MakeEvent("AAPL")).ConfigureAwait(true);
        await writerA.AppendAsync(MakeEvent("MSFT")).ConfigureAwait(true);

        var writerB = await AppendOnlyEventLog<IngestionEvent>.CreateAsync(LogPath)
            .ConfigureAwait(true);
        await writerB.AppendAsync(MakeEvent("VGSH-FROM-SECOND-WRITER-INSTANCE"))
            .ConfigureAwait(true);
        await writerA.AppendAsync(MakeEvent("TLT")).ConfigureAwait(true);
        writerA.Dispose();
        writerB.Dispose();

        // Act — a later kernel instance (blueprint 2's provider) opens the same log.
        // The incident build threw InvalidDataException from recovery here.
        var act = async () =>
        {
            var reader = await AppendOnlyEventLog<IngestionEvent>.CreateAsync(LogPath)
                .ConfigureAwait(true);
            var events = new List<IngestionEvent>();
            await foreach (var (_, evt) in reader.ReadFromAsync(0).ConfigureAwait(true))
            {
                events.Add(evt);
            }

            reader.Dispose();
            return events;
        };

        // Assert
        var replayed = await act.Should().NotThrowAsync().ConfigureAwait(true);
        replayed.Subject.Select(e => e.Symbol).Should().BeEquivalentTo(
            ["AAPL", "MSFT", "VGSH-FROM-SECOND-WRITER-INSTANCE", "TLT"]);
    }
}
