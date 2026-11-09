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

using System.Text.Json;
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.Abstractions.Exceptions;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.Keys;
using Boutquin.MarketData.Cache.Memory;
using Boutquin.MarketData.Cache.Policies;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.Cache.WriteThrough;
using Boutquin.MarketData.Calendars;
using Boutquin.MarketData.Orchestration.Coverage;
using Boutquin.MarketData.Orchestration.Fallback;
using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.MarketData.Orchestration.Snapshots;
using Boutquin.MarketData.Storage.Events;
using Boutquin.MarketData.Storage.InMemory;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.Storage.Domain.ValueObjects;
using Boutquin.Storage.Infrastructure.Events;
using Boutquin.Storage.Infrastructure.Streaming.Checkpointing;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Boutquin.MarketData.Orchestration.Tests.Unit.Pipeline;

internal static class BasisFixture
{
    internal static PriceBasis Tiingo { get; } = new(PriceAdjustment.All, AdjustedCloseSource.ProviderSupplied, PriceAdjustment.None);
    internal static PriceBasis Twelve { get; } = new(PriceAdjustment.All, AdjustedCloseSource.AdapterComputed, PriceAdjustment.Splits);
    internal static DateRange Range { get; } = new(new DateOnly(2024, 6, 3), new DateOnly(2024, 6, 3));
    internal static PriceHistoryRequest Request => new([new Symbol("SPY")], Range) { Basis = Tiingo };
    internal static DataEnvelope<IReadOnlyList<Bar>> Envelope(PriceBasis? basis) =>
        new([new(Range.From, 100m, 101m, 99m, 100m, 100m, 100)], new(1, 1, 0, 1m), [],
            [new(new ProviderCode("fixture"), "SPY", LicenseType.Free, RetrievalMode.Live, FreshnessClass.EndOfDay, DateTimeOffset.UnixEpoch, null) { PriceBasis = basis }]);
    internal static FallbackOrchestrator<PriceHistoryRequest, Bar> Fallback =>
        new(NullLogger<FallbackOrchestrator<PriceHistoryRequest, Bar>>.Instance);
    internal sealed class AcceptAll : IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<Bar>>>
    {
        public bool IsUsable(DataEnvelope<IReadOnlyList<Bar>> response, out string? reason) { reason = null; return true; }
    }
    internal class Undeclared : IDataSourceAdapter<PriceHistoryRequest, Bar>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey => new("fixture");
        public int Priority => 100;
        public int Calls { get; private set; }
        public virtual bool CanHandle(PriceHistoryRequest request) => true;
        public virtual Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(PriceHistoryRequest request, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Envelope(Tiingo)); }
    }
    internal sealed class Declared(PriceBasis basis, PriceBasis? stamp, bool fails = false) : Undeclared, IPriceBasisAdapter
    {
        public PriceBasis DeliveredBasis => basis;
        public override bool CanHandle(PriceHistoryRequest request) => request.Basis == DeliveredBasis;
        public override async Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(PriceHistoryRequest request, CancellationToken cancellationToken = default)
        {
            await base.FetchAsync(request, cancellationToken);
            if (fails) { throw new HttpRequestException("fixture unavailable"); }
            return Envelope(stamp);
        }
    }
    internal sealed class JsonBars : ISnapshotSerializer<IReadOnlyList<Bar>>
    {
        public async Task SerializeAsync(Stream stream, IReadOnlyList<Bar> data, CancellationToken cancellationToken = default) =>
            await JsonSerializer.SerializeAsync(stream, data, cancellationToken: cancellationToken);
        public async Task<IReadOnlyList<Bar>> DeserializeAsync(Stream stream, CancellationToken cancellationToken = default) =>
            (await JsonSerializer.DeserializeAsync<List<Bar>>(stream, cancellationToken: cancellationToken))!;
    }
    internal sealed class Harness : IDisposable
    {
        internal ExactOnceMemoryCache Cache { get; } = new();
        internal WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<Bar>>> L2 { get; }
        internal DataPipeline Pipeline { get; }
        private readonly ServiceProvider _services;
        internal Harness(IReadOnlyList<IDataSourceAdapter<PriceHistoryRequest, Bar>> adapters, bool continueOnFailure = true)
        {
            var store = new InMemorySnapshotStore();
            L2 = new(store, new SnapshotEnvelopeSerializer<IReadOnlyList<Bar>>(new JsonBars()), key => key);
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton(L2);
            foreach (var adapter in adapters) { services.AddSingleton(adapter); }
            _services = services.BuildServiceProvider();
            Pipeline = new(Cache, store, new(new InMemoryCheckpointStore()), new(new InMemoryEventLog<IngestionEvent>()),
                new SnapshotStalenessPolicy(TimeSpan.FromDays(1)), new CoverageCalculator(new WeekendOnlyCalendar()),
                Options.Create(new DataPipelineOptions { ContinueOnPartitionFailure = continueOnFailure }), _services, new FakeClock(), NullLogger<DataPipeline>.Instance);
        }
        internal static string Key => CacheKeyBuilder.FromDateRange("pipeline-v2/Bar/Daily", Request.DatasetKey, Range.From, Range.To);
        internal Task<DataEnvelope<IReadOnlyList<Bar>>> Read() => Pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(Request);
        public void Dispose() { _services.Dispose(); }
    }
}

public sealed class PriceBasisGuardTests
{
    [Fact]
    public async Task Refuses_basis_without_a_declaring_adapter()
    {
        var act = () => BasisFixture.Fallback.ExecuteAsync(BasisFixture.Request, [new BasisFixture.Undeclared()], new BasisFixture.AcceptAll());
        await act.Should().ThrowAsync<PriceBasisNotSupportedException>();
    }
    [Fact]
    public async Task Skips_undeclared_adapter()
    {
        var undeclared = new BasisFixture.Undeclared();
        var result = await BasisFixture.Fallback.ExecuteAsync(BasisFixture.Request,
            [undeclared, new BasisFixture.Declared(BasisFixture.Tiingo, BasisFixture.Tiingo)], new BasisFixture.AcceptAll());
        undeclared.Calls.Should().Be(0);
        result.Attempts.Should().Contain(attempt => attempt.Outcome == "Skipped — no declared price basis" && !attempt.Succeeded);
    }
    [Fact]
    public async Task Rejects_unproven_delivery()
    {
        var act = () => BasisFixture.Fallback.ExecuteAsync(BasisFixture.Request, [new BasisFixture.Declared(BasisFixture.Tiingo, null)], new BasisFixture.AcceptAll());
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Rejected — price basis unproven*");
    }
    [Fact]
    public async Task Rejects_mismatched_delivery()
    {
        var act = () => BasisFixture.Fallback.ExecuteAsync(BasisFixture.Request, [new BasisFixture.Declared(BasisFixture.Tiingo, BasisFixture.Twelve)], new BasisFixture.AcceptAll());
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Rejected — price basis*");
    }
    [Fact]
    public void Merge_rejects_unproven_basis()
    {
        var act = () => EnsureMerged(BasisFixture.Envelope(null).Provenance);
        var exception = act.Should().Throw<PriceBasisMismatchException>().Which;
        exception.Stored.Should().BeNull();
        exception.Provider.Should().Be("partition-merge");
        exception.Message.Should().Contain("price basis unproven");
    }
    [Fact]
    public void Merge_rejects_stated_mismatch()
    {
        var act = () => EnsureMerged(BasisFixture.Envelope(BasisFixture.Twelve).Provenance);
        act.Should().Throw<PriceBasisMismatchException>().Which.Stored.Should().Be(BasisFixture.Twelve);
    }
    [Fact]
    public async Task Unproven_L1_is_refetched()
    {
        var adapter = new BasisFixture.Declared(BasisFixture.Tiingo, BasisFixture.Tiingo);
        using var h = new BasisFixture.Harness([adapter]);
        await h.Cache.SetAsync(BasisFixture.Harness.Key, BasisFixture.Envelope(null), TimeSpan.FromHours(1));
        (await h.Read()).Provenance.Should().OnlyContain(p => p.PriceBasis == BasisFixture.Tiingo);
        adapter.Calls.Should().Be(1);
    }
    [Fact]
    public async Task Mismatched_L1_throws()
    {
        using var h = new BasisFixture.Harness([]);
        await h.Cache.SetAsync(BasisFixture.Harness.Key, BasisFixture.Envelope(BasisFixture.Twelve), TimeSpan.FromHours(1));
        var act = h.Read;
        await act.Should().ThrowAsync<PriceBasisMismatchException>();
    }
    [Fact]
    public async Task L2_unproven_is_refetched_and_mismatch_throws()
    {
        foreach (var stamp in new PriceBasis?[] { null, BasisFixture.Twelve })
        {
            var adapter = new BasisFixture.Declared(BasisFixture.Tiingo, BasisFixture.Tiingo);
            using var h = new BasisFixture.Harness([adapter]);
            var envelope = BasisFixture.Envelope(stamp);
            await h.L2.WriteAsync(BasisFixture.Harness.Key, new(new SchemaVersion(1), new(DateTimeOffset.UtcNow, envelope.Provenance, []), envelope.Payload));
            if (stamp is null)
            {
                (await h.Read()).Provenance.Should().OnlyContain(p => p.PriceBasis == BasisFixture.Tiingo);
                adapter.Calls.Should().Be(1);
                (await h.L2.TryReadAsync(BasisFixture.Harness.Key))!.Metadata.Provenance.Should().OnlyContain(p => p.PriceBasis == BasisFixture.Tiingo);
            }
            else
            {
                var act = h.Read;
                await act.Should().ThrowAsync<PriceBasisMismatchException>();
                adapter.Calls.Should().Be(0);
            }
        }

        await using var snapshot = new DataSnapshot(new DefaultTimeSeriesStoreRegistry(), _ => new StoredRawBinding());
        var readSnapshot = () => snapshot.ExecuteAsync<Bar>(BasisFixture.Request);
        var mismatch = (await readSnapshot.Should().ThrowAsync<PriceBasisMismatchException>()).Which;
        mismatch.Requested.Should().Be(BasisFixture.Tiingo);
        mismatch.Stored.Should().Be(PriceBasis.Raw);
    }
    [Fact]
    public async Task Permissive_custom_evaluator_cannot_admit_mismatch()
    {
        var act = () => BasisFixture.Fallback.ExecuteAsync(BasisFixture.Request, [new BasisFixture.Declared(BasisFixture.Tiingo, BasisFixture.Twelve)], new BasisFixture.AcceptAll());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static void EnsureMerged(IReadOnlyList<DataProvenance> provenance)
    {
        var type = typeof(DataPipeline).Assembly.GetType("Boutquin.MarketData.Orchestration.Pipeline.PriceBasisGuard");
        type.Should().NotBeNull("the fixed merge guard must exist");
        try
        {
            type!.GetMethod("EnsureMerged", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [BasisFixture.Request, provenance]);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(ex.InnerException);
        }
    }

    private sealed class StoredRawBinding : ISnapshotReadBinding<Bar>, IAsyncDisposable
    {
        public Task<IAsyncDisposable> CaptureAsync(ITimeSeriesStoreRegistry registry, CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable>(this);
        public Task<SnapshotReadResult<Bar>> ReadAsync(IAsyncDisposable snapshot, IDataRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SnapshotReadResult<Bar>(BasisFixture.Envelope(PriceBasis.Raw).Payload, false));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class PriceBasisEscapesPartitionCatchTests
{
    [Fact] public Task Refusal_escapes_continue_true() => Refusal(true);
    [Fact] public Task Refusal_escapes_continue_false() => Refusal(false);
    private static async Task Refusal(bool continuation)
    {
        using var h = new BasisFixture.Harness([new BasisFixture.Undeclared()], continuation);
        var act = h.Read;
        await act.Should().ThrowAsync<PriceBasisNotSupportedException>();
    }
    [Fact]
    public async Task Http_failure_remains_partition_failed()
    {
        using var h = new BasisFixture.Harness([new BasisFixture.Declared(BasisFixture.Tiingo, BasisFixture.Tiingo, fails: true)]);
        var result = await h.Read();
        result.Payload.Should().BeEmpty();
        result.Issues.Should().ContainSingle(issue => issue.Code == IssueCode.PartitionFailed);
    }
}

public sealed class PriceBasisNotSupportedMessageTests
{
    [Fact]
    public async Task Undeclared_adapter_refusal_names_migration()
    {
        var fake = new BasisFixture.Undeclared();
        using var h = new BasisFixture.Harness([fake]);
        var exception = (await ((Func<Task>)(async () => await h.Read())).Should().ThrowAsync<PriceBasisNotSupportedException>()).Which;
        exception.Message.Should().Be("No registered price adapter declares price basis 'all.provider.none'. Declared: none. Price adapters must implement IPriceBasisAdapter; see docs/migration-3.0.md.");
        fake.Calls.Should().Be(0);
    }
    [Fact]
    public async Task Declared_list_is_distinct_and_ordinal()
    {
        var act = () => BasisFixture.Fallback.ExecuteAsync(BasisFixture.Request with { Basis = BasisFixture.Twelve },
            [new BasisFixture.Undeclared(), new BasisFixture.Declared(PriceBasis.Raw, PriceBasis.Raw), new BasisFixture.Declared(BasisFixture.Tiingo, BasisFixture.Tiingo)], new BasisFixture.AcceptAll());
        var exception = (await act.Should().ThrowAsync<PriceBasisNotSupportedException>()).Which;
        exception.Message.Should().Be("No registered price adapter declares price basis 'all.computed.splits'. Declared: all.provider.none, none.provider.none. Price adapters must implement IPriceBasisAdapter; see docs/migration-3.0.md.");
        exception.Requested.Should().Be(BasisFixture.Twelve);
        exception.Declared.Should().Equal(BasisFixture.Tiingo, PriceBasis.Raw);
    }
    [Fact]
    public async Task Undeclared_adapter_is_skipped_with_audit()
    {
        var fake = new BasisFixture.Undeclared();
        var result = await BasisFixture.Fallback.ExecuteAsync(BasisFixture.Request, [fake, new BasisFixture.Declared(BasisFixture.Tiingo, BasisFixture.Tiingo)], new BasisFixture.AcceptAll());
        fake.Calls.Should().Be(0);
        result.Attempts.Should().Contain(a => a.Outcome == "Skipped — no declared price basis" && !a.Succeeded);
    }
}
