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

using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.DependencyInjection;
using Boutquin.MarketData.Normalization.Normalizers;
using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.MarketData.Testing.Fakes;
using Boutquin.Storage.Domain.Interfaces.Streaming;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

/// <summary>
/// Proves the cash-dividend fetch path is reachable through the kernel's PRODUCTION dispatch
/// surface — <see cref="IDataPipeline.ExecuteAsync{TRequest, TRecord}"/> — rather than merely
/// unit-constructible. Records must arrive through adapter selection, the normalization stage
/// and envelope assembly; no test here calls an adapter or a normalizer directly in its
/// assertion path.
/// </summary>
public sealed class DividendHistoryPipelineTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-dividends-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Composes the kernel the way a real host does — through
    /// <see cref="ServiceCollectionExtensions.AddMarketDataKernel"/> — and layers on only what a
    /// host owns.
    /// </summary>
    /// <param name="slot">
    /// Per-host storage discriminator. Two hosts inside one test MUST NOT share a snapshot
    /// directory: the pipeline consults the L2 snapshot store before dispatching to any adapter,
    /// so a shared directory would let the first host's persisted result answer the second host's
    /// request and silently vacate every counterfactual in this class.
    /// </param>
    /// <param name="adapter">
    /// The host's dividend data source, or <see langword="null"/> to compose a host that has none.
    /// </param>
    /// <param name="normalizer">
    /// The host's dividend normalizer, or <see langword="null"/> to compose a host that registers
    /// none. Normalizer registration is the HOST's job, not the kernel's: with default options
    /// <c>AddMarketDataKernel</c> supplies no live
    /// <c>INormalizer&lt;DividendPayment, DividendPayment&gt;</c>, so passing
    /// <see langword="null"/> here yields a pipeline that skips the normalization stage entirely.
    /// </param>
    /// <param name="useTimeSeriesStore">Whether the host enables the opt-in time-series path.</param>
    /// <param name="configureServices">Optional registrations applied after the kernel defaults.</param>
    /// <param name="configurePipeline">Optional pipeline options applied after this helper's defaults.</param>
    private ServiceProvider BuildHost(
        string slot,
        IDataSourceAdapter<DividendHistoryRequest, DividendPayment>? adapter = null,
        INormalizer<DividendPayment, DividendPayment>? normalizer = null,
        bool useTimeSeriesStore = false,
        Action<IServiceCollection>? configureServices = null,
        Action<DataPipelineOptions>? configurePipeline = null)
    {
        var root = Path.Combine(_tempDir, slot);
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(root, "snapshots"),
            rawDocumentDirectory: Path.Combine(root, "raw"),
            checkpointDirectory: Path.Combine(root, "checkpoints"),
            eventLogDirectory: Path.Combine(root, "events"),
            timeSeriesDirectory: Path.Combine(root, "timeseries"),
            configurePipeline: options =>
            {
                options.UseTimeSeriesStore = useTimeSeriesStore;
                configurePipeline?.Invoke(options);
            });

        if (adapter is not null)
        {
            services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(adapter);
        }

        if (normalizer is not null)
        {
            services.AddSingleton<INormalizer<DividendPayment, DividendPayment>>(normalizer);
        }

        configureServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The SCHD-shaped regression anchor: the two distributions the charter probe observed in the
    /// live Tiingo payload, as an adapter would emit them.
    /// </summary>
    private static readonly DividendPayment[] s_schdPayments =
    [
        new(new DateOnly(2011, 12, 19), 0.1217m, Currency.USD, DividendType.Unclassified),
        new(new DateOnly(2012, 3, 23), 0.1350m, Currency.USD, DividendType.Unclassified),
    ];

    private static FakeDataSourceAdapter<DividendHistoryRequest, DividendPayment> BuildAdapter(
        IReadOnlyList<DividendPayment> payments)
    {
        var provenance = new DataProvenance(
            new ProviderCode("fake-vendor"), "SCHD", LicenseType.Free, RetrievalMode.Api,
            FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, "https://fake.vendor.test/schd");

        return new FakeDataSourceAdapter<DividendHistoryRequest, DividendPayment>(
            new ProviderCode("fake-vendor"),
            _ => new DataEnvelope<IReadOnlyList<DividendPayment>>(
                payments,
                new DataCoverage(payments.Count, payments.Count, 0, 1.0m),
                [],
                [provenance]));
    }

    private static FakeDataSourceAdapter<DividendHistoryRequest, DividendPayment>
        BuildIdentityOwningAdapter() =>
        new(
            new ProviderCode("identity-owner"),
            request =>
            {
                IReadOnlyList<DividendPayment> payments =
                [
                    new(
                        request.Range.From,
                        request.Symbols.Count,
                        Currency.USD,
                        DividendType.Unclassified),
                ];
                var provenance = new DataProvenance(
                    new ProviderCode("identity-owner"),
                    request.DatasetKey,
                    LicenseType.Free,
                    RetrievalMode.Api,
                    FreshnessClass.EndOfDay,
                    DateTimeOffset.UtcNow,
                    "https://identity-owner.test/dividends");
                return new DataEnvelope<IReadOnlyList<DividendPayment>>(
                    payments,
                    new DataCoverage(1, 1, 0, 1m),
                    [],
                    [provenance]);
            });

    private sealed class WholeDividendRequestPartitioner
        : IRequestPartitioner<DividendHistoryRequest>
    {
        public IReadOnlyList<DividendHistoryRequest> Partition(DividendHistoryRequest request) =>
            [request];
    }

    private sealed class CountingWholeDividendRequestPartitioner
        : IRequestPartitioner<DividendHistoryRequest>
    {
        public int Calls { get; private set; }

        public IReadOnlyList<DividendHistoryRequest> Partition(DividendHistoryRequest request)
        {
            Calls++;
            return [request];
        }
    }

    private static DividendHistoryRequest SchdRequest() =>
        new([new Symbol("SCHD")], new DateRange(new DateOnly(2011, 10, 1), new DateOnly(2012, 3, 31)));

    /// <summary>
    /// Deliberately un-normalized payments: a negative amount (which
    /// <see cref="DividendPaymentNormalizer"/> clamps to zero) and an amount carrying more
    /// precision than the four decimal places it rounds to. Both values differ from their
    /// normalized form, so an assertion on the normalized form cannot pass by coincidence.
    /// </summary>
    private static readonly DividendPayment[] s_rawPayments =
    [
        new(new DateOnly(2024, 2, 1), -0.05m, Currency.USD, DividendType.Unclassified),
        new(new DateOnly(2024, 5, 1), 0.123456789m, Currency.USD, DividendType.Unclassified),
    ];

    /// <summary>
    /// An adapter that fetched successfully and found no distributions: empty payload, complete
    /// coverage, no issues — exactly the envelope the shipped Tiingo dividend adapter emits for a
    /// window containing no ex-dates.
    /// </summary>
    private static FakeDataSourceAdapter<DividendHistoryRequest, DividendPayment> BuildEmptyWindowAdapter()
    {
        var provenance = new DataProvenance(
            new ProviderCode("fake-vendor"), "VYM", LicenseType.Free, RetrievalMode.Api,
            FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, "https://fake.vendor.test/vym");

        return new FakeDataSourceAdapter<DividendHistoryRequest, DividendPayment>(
            new ProviderCode("fake-vendor"),
            _ => new DataEnvelope<IReadOnlyList<DividendPayment>>(
                [],
                new DataCoverage(0, 0, 0, 1.0m),
                [],
                [provenance]));
    }

    private static DividendHistoryRequest VymRequest() =>
        new([new Symbol("VYM")], new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Sparse_extension_preserves_event_completeness(bool emptySuffix, bool partialSuffix)
    {
        var from = new DateOnly(2024, 1, 1);
        var middle = new DateOnly(2024, 6, 30);
        var to = new DateOnly(2024, 12, 31);
        var adapter = new TestDividendAdapter("sparse", 100, request =>
        {
            var first = request.Range.From == from;
            DividendPayment[] rows = !first && emptySuffix ? [] : [Payment(request.Range.From.AddDays(20))];
            var missing = !first && partialSuffix ? 1 : 0;
            return DividendEnvelope(rows,
                new DataCoverage(rows.Length + missing, rows.Length, missing, missing == 0 ? 1m : 0.5m), "sparse");
        });
        using var host = BuildHost("sparse-extension", adapter, useTimeSeriesStore: true,
            configurePipeline: options =>
            {
                options.L1CacheTtl = TimeSpan.Zero;
                options.TimeSeriesStalenessMaxAge = TimeSpan.MaxValue;
            });
        var pipeline = host.GetRequiredService<IDataPipeline>();
        var initial = new DividendHistoryRequest([new Symbol("VYM")], new DateRange(from, middle));
        _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(initial).ConfigureAwait(true);
        var result = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(
            initial with { Range = new DateRange(from, to) }).ConfigureAwait(true);

        if (partialSuffix)
        {
            result.Coverage.Status.Should().NotBe(CompletenessStatus.Complete);
        }
        else
        {
            result.Coverage.Should().Be(new DataCoverage(emptySuffix ? 1 : 2, emptySuffix ? 1 : 2, 0, 1m));
            result.Payload.Should().HaveCount(emptySuffix ? 1 : 2);
        }
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public async Task Time_series_source_metadata_survives_live_store_and_restart(int stage, bool checkIssues)
    {
        var date = new DateOnly(2024, 2, 20);
        var warning = new DataIssue(IssueCode.MissingField, IssueSeverity.Warning, "source-quality-warning");
        var provenance = new DataProvenance(new ProviderCode("licensed-source"), "dividends",
            LicenseType.Commercial, RetrievalMode.Api, FreshnessClass.EndOfDay,
            new DateTimeOffset(2024, 2, 21, 1, 0, 0, TimeSpan.Zero), "https://licensed.test/data", date);
        var adapter = new TestDividendAdapter("licensed-source", 100,
            _ => new DataEnvelope<IReadOnlyList<DividendPayment>>([Payment(date)], new(1, 1, 0, 1m), [warning], [provenance]));
        var request = new DividendHistoryRequest([new Symbol("VYM")], new DateRange(date, date));
        DataEnvelope<IReadOnlyList<DividendPayment>> result;
        using (var writer = BuildHost("source-metadata", adapter, useTimeSeriesStore: true,
            configurePipeline: options =>
            {
                options.L1CacheTtl = TimeSpan.Zero;
                options.TimeSeriesStalenessMaxAge = TimeSpan.MaxValue;
            }))
        {
            var pipeline = writer.GetRequiredService<IDataPipeline>();
            result = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request).ConfigureAwait(true);
            if (stage == 1)
            {
                result = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request).ConfigureAwait(true);
            }
        }

        if (stage == 2)
        {
            using var reader = BuildHost("source-metadata", useTimeSeriesStore: true,
                configurePipeline: options => options.TimeSeriesStalenessMaxAge = TimeSpan.MaxValue);
            result = await reader.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request).ConfigureAwait(true);
        }

        result.Payload.Should().ContainSingle();
        if (checkIssues)
        {
            result.Issues.Should().Contain(warning);
        }
        else
        {
            result.Provenance.Should().ContainSingle().Which.Should().Be(
                provenance with { RetrievalMode = RetrievalMode.Cache, SourceUrl = null });
        }
    }

    [Fact]
    public async Task Direct_time_series_rows_do_not_fabricate_redistribution_rights()
    {
        var date = new DateOnly(2024, 2, 20);
        using var host = BuildHost("unknown-source", useTimeSeriesStore: true,
            configurePipeline: options => options.TimeSeriesStalenessMaxAge = TimeSpan.MaxValue);
        await host.GetRequiredService<ITimeSeriesStoreRegistry>().Resolve<DividendPaymentDatasetKey, DividendPayment>()!
            .AppendAsync(new DividendPaymentDatasetKey("default", "VYM"), [Payment(date)]).ConfigureAwait(true);
        var result = await host.GetRequiredService<IDataPipeline>().ExecuteAsync<DividendHistoryRequest, DividendPayment>(
            new([new Symbol("VYM")], new DateRange(date, date))).ConfigureAwait(true);
        result.Provenance.Should().ContainSingle().Which.LicenseFlag.ToString().Should().Be("Unknown");
    }

    private static DividendPayment Payment(DateOnly date, decimal amount = 1m) =>
        new(date, amount, Currency.USD, DividendType.Unclassified);

    private static DataEnvelope<IReadOnlyList<DividendPayment>> DividendEnvelope(
        IReadOnlyList<DividendPayment> payments,
        DataCoverage coverage,
        string provider) =>
        new(
            payments,
            coverage,
            [],
            [new DataProvenance(
                new ProviderCode(provider),
                "dividends",
                LicenseType.Free,
                RetrievalMode.Api,
                FreshnessClass.EndOfDay,
                DateTimeOffset.UtcNow,
                $"https://{provider}.test/dividends")]);

    private sealed class TestDividendAdapter(
        string provider,
        int priority,
        Func<DividendHistoryRequest, DataEnvelope<IReadOnlyList<DividendPayment>>> responseFactory)
        : IDataSourceAdapter<DividendHistoryRequest, DividendPayment>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new(provider);

        public int Priority { get; } = priority;

        public List<DividendHistoryRequest> Invocations { get; } = [];

        public bool CanHandle(DividendHistoryRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<DividendPayment>>> FetchAsync(
            DividendHistoryRequest request,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class RangeAwareDividendAdapter(
        string provider,
        int priority,
        Func<DividendHistoryRequest, bool> canHandle,
        Func<DividendHistoryRequest, DataEnvelope<IReadOnlyList<DividendPayment>>> responseFactory)
        : IDataSourceAdapter<DividendHistoryRequest, DividendPayment>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new(provider);

        public int Priority { get; } = priority;

        public List<DividendHistoryRequest> Invocations { get; } = [];

        public bool CanHandle(DividendHistoryRequest request) => canHandle(request);

        public Task<DataEnvelope<IReadOnlyList<DividendPayment>>> FetchAsync(
            DividendHistoryRequest request,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class RejectingDividendAdapter :
        IDataSourceAdapter<DividendHistoryRequest, DividendPayment>
    {
        public ProviderCode ProviderKey => new("venue-rejecting");

        public int FetchCount { get; private set; }

        public bool CanHandle(DividendHistoryRequest request) => false;

        public Task<DataEnvelope<IReadOnlyList<DividendPayment>>> FetchAsync(
            DividendHistoryRequest request,
            CancellationToken cancellationToken = default)
        {
            FetchCount++;
            throw new InvalidOperationException("FetchAsync must not run after CanHandle returned false.");
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
    }

    private sealed class ObservingAttemptedRangeIndex(
        int capacity,
        TimeSpan ttl,
        IClock clock) : IAttemptedRangeIndex
    {
        private readonly InMemoryAttemptedRangeIndex _inner = new(capacity, ttl, clock);
        private (DateOnly From, DateOnly To)? _seedNextLookup;

        public List<string> Lookups { get; } = [];

        public List<string> RecordedIdentities { get; } = [];

        public void SeedNextLookup(DateOnly from, DateOnly to) => _seedNextLookup = (from, to);

        public IReadOnlyList<(DateOnly From, DateOnly To)> GetAttemptedRanges(string datasetIdentity)
        {
            Lookups.Add(datasetIdentity);
            if (_seedNextLookup is { } seed)
            {
                _inner.RecordAttempt(datasetIdentity, seed.From, seed.To);
                _seedNextLookup = null;
            }

            return _inner.GetAttemptedRanges(datasetIdentity);
        }

        public void RecordAttempt(string datasetIdentity, DateOnly from, DateOnly to)
        {
            RecordedIdentities.Add(datasetIdentity);
            _inner.RecordAttempt(datasetIdentity, from, to);
        }
    }

    private sealed class ObservingCheckpointStore : ICheckpointStore
    {
        private readonly Dictionary<string, (long Offset, byte[] State)> _checkpoints =
            new(StringComparer.Ordinal);

        public List<string> LoadedProcessorIds { get; } = [];

        public List<string> SavedProcessorIds { get; } = [];

        public void ClearObservations()
        {
            LoadedProcessorIds.Clear();
            SavedProcessorIds.Clear();
        }

        public ValueTask SaveAsync(
            string processorId,
            long offset,
            byte[] state,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedProcessorIds.Add(processorId);
            _checkpoints[processorId] = (offset, state.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask<(long Offset, byte[] State)?> LoadAsync(
            string processorId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LoadedProcessorIds.Add(processorId);
            return _checkpoints.TryGetValue(processorId, out var checkpoint)
                ? ValueTask.FromResult<(long Offset, byte[] State)?>((checkpoint.Offset, checkpoint.State.ToArray()))
                : ValueTask.FromResult<(long Offset, byte[] State)?>(null);
        }
    }

    private sealed class CancelOnSecondPartitionDividendAdapter(CancellationTokenSource cancellation)
        : IDataSourceAdapter<DividendHistoryRequest, DividendPayment>
    {
        public ProviderCode ProviderKey => new("checkpoint-provider");

        public List<string> Invocations { get; } = [];

        public bool CanHandle(DividendHistoryRequest request) => true;

        public Task<DataEnvelope<IReadOnlyList<DividendPayment>>> FetchAsync(
            DividendHistoryRequest request,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(request.Symbols.Single().Ticker);
            if (Invocations.Count == 2)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(DividendEnvelope([], new DataCoverage(0, 0, 0, 1m), ProviderKey.Value));
        }
    }

    // ---------- AC-4.1: host-side normalizer registration ----------

    /// <summary>
    /// AC-4.1. Dividend normalization follows the same host-side convention as every sibling
    /// record: the host registers <see cref="DividendPaymentNormalizer"/> and the pipeline's own
    /// provider resolves it. Both directions are pinned in one test — with the host registration
    /// the resolved instance IS the host's, and without it nothing resolves at all — because the
    /// non-null half alone could not distinguish a host registration that works from one that is
    /// quietly redundant.
    /// </summary>
    /// <remarks>
    /// The resolution asserted here is the same call the pipeline makes when it assembles the
    /// normalization stage: <c>GetService&lt;INormalizer&lt;TRecord, TRecord&gt;&gt;()</c> on the
    /// provider it was built from, closed at <see cref="DividendPayment"/>.
    /// </remarks>
    [Fact]
    public void Host_side_registration_is_what_makes_the_dividend_normalizer_resolve()
    {
        var hostNormalizer = new DividendPaymentNormalizer();

        using var withHostRegistration = BuildHost("normalizer-registered", normalizer: hostNormalizer);
        withHostRegistration.GetService<INormalizer<DividendPayment, DividendPayment>>()
            .Should().NotBeNull("the host registered a dividend normalizer")
            .And.BeSameAs(hostNormalizer,
                "the pipeline must resolve the HOST's normalizer — a different instance would mean "
                + "the host registration was overridden and is not the thing doing the work");

        using var withoutHostRegistration = BuildHost("normalizer-absent");
        withoutHostRegistration.GetService<INormalizer<DividendPayment, DividendPayment>>()
            .Should().BeNull(
                "with default options the kernel supplies no live dividend normalizer, so the "
                + "host-side registration is load-bearing rather than decorative");
    }

    // ---------- AC-4.2: the anti-inert gate ----------

    /// <summary>
    /// AC-4.2. The capability must be reachable by asking the pipeline, not by newing an adapter.
    /// Every record asserted below arrived through adapter selection inside
    /// <c>ExecuteAsync</c>; the adapter's own invocation log is asserted so a green here cannot be
    /// produced by a cache hit or by an envelope the test itself assembled.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_returns_the_expected_dividend_set_through_production_dispatch()
    {
        var adapter = BuildAdapter(s_schdPayments);
        using var sp = BuildHost("dispatch", adapter);
        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var result = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(SchdRequest())
            .ConfigureAwait(true);

        adapter.Invocations.Should().ContainSingle(
            "the records below must have been produced by the pipeline calling the registered "
            + "adapter — not by a cache hit, and not by an envelope this test assembled itself");

        result.Payload.Should().HaveCount(2,
            "the pipeline must dispatch the dividend request to the registered adapter and return "
            + "both distributions through the production path");
        result.Payload[0].ExDate.Should().Be(new DateOnly(2011, 12, 19));
        result.Payload[0].Amount.Should().Be(0.1217m);
        result.Payload[0].Currency.Should().Be(Currency.USD);
        result.Payload[0].Type.Should().Be(DividendType.Unclassified);
        result.Payload[1].ExDate.Should().Be(new DateOnly(2012, 3, 23));
        result.Payload[1].Amount.Should().Be(0.1350m);
    }

    [Fact]
    public async Task ExecuteAsync_keeps_opaque_single_ticker_and_matching_basket_separate_in_L1_and_L2()
    {
        var range = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var opaque = new DividendHistoryRequest([new Symbol("A_B")], range);
        var basket = new DividendHistoryRequest([new Symbol("A"), new Symbol("B")], range);
        var liveAdapter = BuildIdentityOwningAdapter();

        using (var liveHost = BuildHost(
            "injective-request-identity",
            liveAdapter,
            configureServices: services =>
                services.AddSingleton<IRequestPartitioner<DividendHistoryRequest>, WholeDividendRequestPartitioner>()))
        {
            var pipeline = liveHost.GetRequiredService<IDataPipeline>();
            var opaqueResult = await pipeline
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(opaque)
                .ConfigureAwait(true);
            var basketResult = await pipeline
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(basket)
                .ConfigureAwait(true);

            opaque.DatasetKey.Should().NotBe(basket.DatasetKey);
            liveAdapter.Invocations.Should().HaveCount(2,
                "both logical requests must dispatch instead of the basket taking the opaque ticker's L1 entry");
            opaqueResult.Payload.Should().ContainSingle().Which.Amount.Should().Be(1m);
            basketResult.Payload.Should().ContainSingle().Which.Amount.Should().Be(2m);
            basketResult.Provenance.Should().ContainSingle()
                .Which.Dataset.Should().Be(basket.DatasetKey,
                    "the second payload must be owned by the basket request");
        }

        var replayAdapter = BuildIdentityOwningAdapter();
        using var replayHost = BuildHost(
            "injective-request-identity",
            replayAdapter,
            configureServices: services =>
                services.AddSingleton<IRequestPartitioner<DividendHistoryRequest>, WholeDividendRequestPartitioner>());
        var replayPipeline = replayHost.GetRequiredService<IDataPipeline>();
        var opaqueReplay = await replayPipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(opaque)
            .ConfigureAwait(true);
        var basketReplay = await replayPipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(basket)
            .ConfigureAwait(true);

        replayAdapter.Invocations.Should().BeEmpty(
            "a fresh host must resolve both distinct identities from their L2 snapshots");
        opaqueReplay.Payload.Should().ContainSingle().Which.Amount.Should().Be(1m);
        basketReplay.Payload.Should().ContainSingle().Which.Amount.Should().Be(2m);
        basketReplay.Provenance.Should().ContainSingle()
            .Which.Dataset.Should().Be(basket.DatasetKey);
    }

    [Fact]
    public async Task Checkpoint_resume_isolated_complete_dividend_requests_by_hostile_key_and_range()
    {
        var equalRange = new DateRange(new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 2));
        var shiftedRange = new DateRange(new DateOnly(2024, 2, 2), new DateOnly(2024, 2, 3));
        var cases = new[]
        {
            (Slot: "slash", Request: new DividendHistoryRequest(
                [new Symbol("A/B"), new Symbol("TAIL")], equalRange)),
            (Slot: "underscores", Request: new DividendHistoryRequest(
                [new Symbol("A__B"), new Symbol("TAIL")], equalRange)),
            (Slot: "shifted", Request: new DividendHistoryRequest(
                [new Symbol("A/B"), new Symbol("TAIL")], shiftedRange)),
        };
        var checkpointStore = new ObservingCheckpointStore();
        var processorIds = new List<string>();

        cases[0].Request.DatasetKey.Should().NotBe(cases[1].Request.DatasetKey,
            "the complete dividend request encoding already distinguishes the hostile logical keys");

        foreach (var testCase in cases)
        {
            checkpointStore.ClearObservations();
            using var cancellation = new CancellationTokenSource();
            var interrupted = new CancelOnSecondPartitionDividendAdapter(cancellation);
            using (var crashingHost = BuildHost(
                $"checkpoint-{testCase.Slot}",
                configureServices: services =>
                {
                    services.AddSingleton<ICheckpointStore>(checkpointStore);
                    services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(interrupted);
                }))
            {
                Func<Task> crash = async () =>
                    await crashingHost.GetRequiredService<IDataPipeline>()
                        .ExecuteAsync<DividendHistoryRequest, DividendPayment>(
                            testCase.Request,
                            cancellation.Token)
                        .ConfigureAwait(true);

                await crash.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
            }

            checkpointStore.LoadedProcessorIds.Should().ContainSingle(
                "each execution loads exactly the identity of its complete dividend request");
            checkpointStore.SavedProcessorIds.Should().ContainSingle(
                "only the successful first partition is checkpointed before cancellation");
            checkpointStore.SavedProcessorIds.Single().Should().Be(checkpointStore.LoadedProcessorIds.Single());
            processorIds.Add(checkpointStore.LoadedProcessorIds.Single());

            checkpointStore.ClearObservations();
            var resumed = new TestDividendAdapter(
                "checkpoint-provider",
                100,
                _ => DividendEnvelope([], new DataCoverage(0, 0, 0, 1m), "checkpoint-provider"));
            using var resumeHost = BuildHost(
                $"checkpoint-{testCase.Slot}",
                configureServices: services =>
                {
                    services.AddSingleton<ICheckpointStore>(checkpointStore);
                    services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(resumed);
                });

            _ = await resumeHost.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(testCase.Request)
                .ConfigureAwait(true);

            checkpointStore.LoadedProcessorIds.Should().ContainSingle()
                .Which.Should().Be(processorIds[^1],
                    "resume must load the checkpoint written by this exact request");
            resumed.Invocations.Select(request => request.Symbols.Single().Ticker)
                .Should().Equal(testCase.Request.Symbols.Select(symbol => symbol.Ticker),
                    "safe legacy resume starts at partition zero and cannot consume a foreign prefix");
        }

        processorIds.Should().OnlyHaveUniqueItems(
            "A/B versus A__B at one range and A/B at a new range need independent load identities");
    }

    /// <summary>
    /// AC-4.2, counterfactual. The discriminator for the test above: strip the one thing that
    /// makes the capability live — the registered
    /// <c>IDataSourceAdapter&lt;DividendHistoryRequest, DividendPayment&gt;</c> — and the same
    /// call returns nothing. Without this, a green above could not distinguish "the pipeline
    /// dispatched to the adapter" from "something else supplied the records".
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_returns_no_dividends_when_no_dividend_adapter_is_registered()
    {
        using var sp = BuildHost("no-adapter");
        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var result = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(SchdRequest())
            .ConfigureAwait(true);

        result.Payload.Should().BeEmpty(
            "no adapter can serve the request, so the production path has nothing to return");
        result.Issues.Should().Contain(i => i.Code == IssueCode.PartitionFailed,
            "an unservable partition is reported as a failure rather than as an empty success");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Consumer_partitioner_has_legacy_and_time_series_production_parity(int symbolCount)
    {
        var symbols = new[] { new Symbol("AAPL"), new Symbol("SCHD"), new Symbol("VYM") }
            .Take(symbolCount)
            .ToArray();
        var request = new DividendHistoryRequest(
            symbols,
            new DateRange(new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2)));
        var observations = new List<(
            bool UseTimeSeriesStore,
            int PartitionerCalls,
            IReadOnlyList<DividendHistoryRequest> AdapterInvocations,
            DataEnvelope<IReadOnlyList<DividendPayment>> Result)>();

        foreach (var useTimeSeriesStore in new[] { false, true })
        {
            var partitioner = new CountingWholeDividendRequestPartitioner();
            var adapter = new TestDividendAdapter(
                "partitioner-vendor",
                100,
                partition => new DataEnvelope<IReadOnlyList<DividendPayment>>(
                    [Payment(partition.Range.From, partition.Symbols.Count)],
                    new DataCoverage(1, 1, 0, 1m),
                    [],
                    [new DataProvenance(
                        new ProviderCode("partitioner-vendor"),
                        partition.DatasetKey,
                        LicenseType.Free,
                        RetrievalMode.Api,
                        FreshnessClass.EndOfDay,
                        new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero),
                        "https://partitioner-vendor.test/dividends")]));
            using var sp = BuildHost(
                $"consumer-partitioner-parity-{symbolCount}-{useTimeSeriesStore}",
                adapter,
                useTimeSeriesStore: useTimeSeriesStore,
                configureServices: services =>
                    services.AddSingleton<IRequestPartitioner<DividendHistoryRequest>>(partitioner));

            var result = await sp.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
                .ConfigureAwait(true);

            observations.Add((useTimeSeriesStore, partitioner.Calls, adapter.Invocations.ToList(), result));
        }

        foreach (var observation in observations)
        {
            observation.PartitionerCalls.Should().Be(1,
                $"the consumer policy is authoritative in time-series={observation.UseTimeSeriesStore}");
            observation.AdapterInvocations.Should().ContainSingle();
            observation.AdapterInvocations[0].Symbols.Should().Equal(symbols,
                $"time-series={observation.UseTimeSeriesStore} must dispatch the whole selected partition");
        }

        var legacy = observations.Single(observation => !observation.UseTimeSeriesStore).Result;
        var timeSeries = observations.Single(observation => observation.UseTimeSeriesStore).Result;
        timeSeries.Payload.Should().Equal(legacy.Payload);
        timeSeries.Coverage.Should().Be(legacy.Coverage);
        timeSeries.Issues.Should().Equal(legacy.Issues);
        timeSeries.Provenance.Should().BeEquivalentTo(
            legacy.Provenance,
            options => options.Excluding(provenance => provenance.RetrievalMode),
            "provider, dataset, license, freshness, timestamp, and source ownership must remain equivalent");
        legacy.Provenance.Should().OnlyContain(provenance => provenance.RetrievalMode == RetrievalMode.Api);
        timeSeries.Provenance.Should().OnlyContain(provenance => provenance.RetrievalMode == RetrievalMode.Api,
            "a multi-symbol dividend partition is intentionally dispatched through the legacy path "
            + "because its rows have no symbol coordinate for physical-store attribution");
    }

    [Fact]
    public async Task Time_series_consumer_preserved_batch_leaves_physical_dividend_series_clean()
    {
        var symbols = new[] { new Symbol("AAPL"), new Symbol("MSFT") };
        var request = new DividendHistoryRequest(
            symbols,
            new DateRange(new DateOnly(2024, 4, 1), new DateOnly(2024, 4, 2)));
        var partitioner = new CountingWholeDividendRequestPartitioner();
        var adapter = new TestDividendAdapter(
            "whole-partition-vendor",
            100,
            partition => DividendEnvelope(
                [Payment(partition.Range.From, partition.Symbols.Count)],
                new DataCoverage(1, 1, 0, 1m),
                "whole-partition-vendor"));
        using var sp = BuildHost(
            "whole-partition-physical-cleanliness",
            adapter,
            useTimeSeriesStore: true,
            configureServices: services =>
                services.AddSingleton<IRequestPartitioner<DividendHistoryRequest>>(partitioner));

        var result = await sp.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        partitioner.Calls.Should().Be(1, "the consumer partitioning policy is evaluated once per request");
        adapter.Invocations.Should().ContainSingle();
        adapter.Invocations[0].Symbols.Should().Equal(symbols);
        result.Payload.Should().ContainSingle().Which.Amount.Should().Be(2m);
        result.Provenance.Should().OnlyContain(provenance => provenance.RetrievalMode == RetrievalMode.Api,
            "the ambiguous partition is dispatched without entering the time-series store");

        var store = sp.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<DividendPaymentDatasetKey, DividendPayment>();
        store.Should().NotBeNull();
        foreach (var symbol in symbols)
        {
            var persisted = new List<DividendPayment>();
            await foreach (var payment in store!
                .GetRangeAsync(
                    new DividendPaymentDatasetKey("whole-partition-vendor", symbol.Ticker),
                    request.Range.From,
                    request.Range.To)
                .ConfigureAwait(true))
            {
                persisted.Add(payment);
            }

            persisted.Should().BeEmpty(
                $"a payment without row-level symbol identity cannot be attributed to {symbol.Ticker}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consumer_partitioner_preserves_cancellation_in_legacy_and_time_series_modes(
        bool useTimeSeriesStore)
    {
        using var cancellation = new CancellationTokenSource();
        var partitioner = new CountingWholeDividendRequestPartitioner();
        var adapter = new TestDividendAdapter(
            "partitioner-cancellation-vendor",
            100,
            _ => throw Cancel(cancellation));
        var request = new DividendHistoryRequest(
            [new Symbol("AAPL"), new Symbol("SCHD")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));
        using var sp = BuildHost(
            $"consumer-partitioner-cancellation-{useTimeSeriesStore}",
            adapter,
            useTimeSeriesStore: useTimeSeriesStore,
            configureServices: services =>
                services.AddSingleton<IRequestPartitioner<DividendHistoryRequest>>(partitioner));

        Func<Task> act = async () => _ = await sp.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request, cancellation.Token)
            .ConfigureAwait(true);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
        partitioner.Calls.Should().Be(1);
        adapter.Invocations.Should().ContainSingle();
        adapter.Invocations[0].Symbols.Should().Equal(request.Symbols);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Default_partitioner_preserves_per_symbol_dispatch_in_legacy_and_time_series_modes(
        int symbolCount)
    {
        var symbols = new[] { new Symbol("AAPL"), new Symbol("SCHD"), new Symbol("VYM") }
            .Take(symbolCount)
            .ToArray();
        var request = new DividendHistoryRequest(
            symbols,
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));

        foreach (var useTimeSeriesStore in new[] { false, true })
        {
            var adapter = new TestDividendAdapter(
                "default-partitioner-vendor",
                100,
                partition => DividendEnvelope(
                    [Payment(partition.Range.From)],
                    new DataCoverage(1, 1, 0, 1m),
                    "default-partitioner-vendor"));
            using var sp = BuildHost(
                $"default-partitioner-parity-{symbolCount}-{useTimeSeriesStore}",
                adapter,
                useTimeSeriesStore: useTimeSeriesStore);

            _ = await sp.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
                .ConfigureAwait(true);

            adapter.Invocations.Select(invocation => invocation.Symbols.Single().Ticker)
                .Should().Equal(
                    symbols.Select(symbol => symbol.Ticker),
                    $"time-series={useTimeSeriesStore} must preserve default per-symbol dispatch");
        }
    }

    [Fact]
    public async Task Time_series_continues_after_a_partition_failure_when_configured()
    {
        var adapter = new TestDividendAdapter(
            "partition-failure-vendor",
            100,
            request => request.Symbols.Single().Ticker == "AAPL"
                ? throw new InvalidOperationException("adapter credential=/private/secret")
                : DividendEnvelope(
                    [Payment(request.Range.From)],
                    new DataCoverage(1, 1, 0, 1m),
                    "partition-failure-vendor"));
        var request = new DividendHistoryRequest(
            [new Symbol("AAPL"), new Symbol("SCHD")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));

        using var sp = BuildHost(
            "partition-failure-continue-time-series",
            adapter,
            useTimeSeriesStore: true,
            configurePipeline: options => options.ContinueOnPartitionFailure = true);

        var result = await sp.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        adapter.Invocations.Select(invocation => invocation.Symbols.Single().Ticker)
            .Should().Equal("AAPL", "SCHD");
        result.Payload.Should().ContainSingle(payment => payment.ExDate == request.Range.From);
        result.Coverage.Should().Be(new DataCoverage(2, 1, 1, 0.5m),
            "a failed selected partition contributes missing coverage instead of leaving its complete sibling authoritative");
        result.Issues.Should().ContainSingle(issue => issue.Code == IssueCode.PartitionFailed)
            .Which.Message.Should().NotContain("/private/secret");
    }

    [Fact]
    public async Task Time_series_stops_after_a_partition_failure_when_configured()
    {
        var adapter = new TestDividendAdapter(
            "partition-stop-vendor",
            100,
            request => request.Symbols.Single().Ticker == "AAPL"
                ? throw new InvalidOperationException("adapter credential=/private/secret")
                : DividendEnvelope(
                    [Payment(request.Range.From)],
                    new DataCoverage(1, 1, 0, 1m),
                    "partition-stop-vendor"));
        var request = new DividendHistoryRequest(
            [new Symbol("AAPL"), new Symbol("SCHD")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));

        using var sp = BuildHost(
            "partition-failure-stop-time-series",
            adapter,
            useTimeSeriesStore: true,
            configurePipeline: options => options.ContinueOnPartitionFailure = false);

        var result = await sp.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        adapter.Invocations.Select(invocation => invocation.Symbols.Single().Ticker)
            .Should().Equal("AAPL");
        result.Payload.Should().BeEmpty();
        result.Issues.Should().ContainSingle(issue => issue.Code == IssueCode.PartitionFailed);
    }

    [Fact]
    public async Task Time_series_partition_cancellation_propagates_without_dispatching_later_partitions()
    {
        using var cancellation = new CancellationTokenSource();
        var adapter = new TestDividendAdapter(
            "partition-cancellation-vendor",
            100,
            request => request.Symbols.Single().Ticker == "AAPL"
                ? throw Cancel(cancellation)
                : DividendEnvelope(
                    [Payment(request.Range.From)],
                    new DataCoverage(1, 1, 0, 1m),
                    "partition-cancellation-vendor"));
        var request = new DividendHistoryRequest(
            [new Symbol("AAPL"), new Symbol("SCHD")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));

        using var sp = BuildHost("partition-cancellation-time-series", adapter, useTimeSeriesStore: true);
        Func<Task> act = async () => _ = await sp.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request, cancellation.Token)
            .ConfigureAwait(true);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
        adapter.Invocations.Select(invocation => invocation.Symbols.Single().Ticker)
            .Should().Equal("AAPL");
    }

    private static OperationCanceledException Cancel(CancellationTokenSource cancellation)
    {
        cancellation.Cancel();
        return new OperationCanceledException(cancellation.Token);
    }

    // ---------- AC-4.3: the normalizer's effect, observed end-to-end ----------

    /// <summary>
    /// AC-4.3. The normalization stage is part of the production dispatch path, so a host that
    /// registers <see cref="DividendPaymentNormalizer"/> must see its two transformations —
    /// negative amounts clamped to zero, amounts rounded to four decimal places — in the records
    /// <c>ExecuteAsync</c> hands back. Asserted on values that differ from the adapter's raw
    /// output, so the green cannot be produced by the records simply passing through.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_applies_the_host_registered_normalizer_end_to_end()
    {
        using var sp = BuildHost(
            "normalized", BuildAdapter(s_rawPayments), new DividendPaymentNormalizer());
        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var result = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(VymRequest())
            .ConfigureAwait(true);

        result.Payload.Should().HaveCount(2);
        result.Payload[0].Amount.Should().Be(0m,
            "the normalizer clamps a negative distribution amount to zero");
        result.Payload[1].Amount.Should().Be(0.1235m,
            "the normalizer rounds the amount to four decimal places, away from zero");
    }

    /// <summary>
    /// AC-4.3, counterfactual. Identical adapter, identical request, identical call — the ONLY
    /// difference is that this host registers no normalizer, and the raw amounts survive
    /// unchanged. This is what makes the assertion above load-bearing rather than incidentally
    /// equal, and it pins the kernel's documented posture: with default options
    /// <c>AddMarketDataKernel</c> leaves the normalization stage inactive, so normalizing dividend
    /// records is a decision the host makes, not one the kernel makes for it.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_leaves_amounts_un_normalized_when_the_host_registers_no_normalizer()
    {
        using var sp = BuildHost("un-normalized", BuildAdapter(s_rawPayments));
        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var result = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(VymRequest())
            .ConfigureAwait(true);

        result.Payload.Should().HaveCount(2);
        result.Payload[0].Amount.Should().Be(-0.05m, "nothing clamped this amount");
        result.Payload[1].Amount.Should().Be(0.123456789m, "nothing rounded this amount");
    }

    // ---------- Empty dividend window: an ordinary outcome, not a failure ----------

    /// <summary>
    /// A symbol that paid nothing in the requested window is the ordinary case for a non-payer,
    /// and the adapter reports it honestly: an empty payload with complete coverage. That must
    /// reach the caller as an empty SUCCESS. Reporting it as a failed partition would make "this
    /// ETF pays no dividends" indistinguishable from "the provider is down" — and a trailing-yield
    /// consumer cannot tell a true zero from missing data.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_reports_an_empty_dividend_window_as_success_not_failure()
    {
        var adapter = BuildEmptyWindowAdapter();
        using var sp = BuildHost("empty-window", adapter);
        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var result = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(VymRequest())
            .ConfigureAwait(true);

        adapter.Invocations.Should().ContainSingle(
            "the adapter answered the request — this is a real fetch, not an unservable partition");
        result.Payload.Should().BeEmpty("the symbol distributed nothing in this window");
        result.Issues.Should().NotContain(i => i.Severity == IssueSeverity.Error,
            "an empty-but-complete dividend window is a legitimate answer, so nothing about it is "
            + "an error the caller must react to");
    }

    [Fact]
    public async Task Legacy_snapshot_replay_preserves_complete_event_sparse_coverage_across_hosts()
    {
        var request = new DividendHistoryRequest(
            [new Symbol("DGRO")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)));
        DividendPayment payment = new(
            new DateOnly(2024, 3, 22), 0.31m, Currency.USD, DividendType.Unclassified);

        using (var liveHost = BuildHost("durable-complete-nonempty", BuildAdapter([payment])))
        {
            var live = await liveHost.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
                .ConfigureAwait(true);

            live.Coverage.Status.Should().Be(CompletenessStatus.Complete);
        }

        var nonemptyReplayAdapter = BuildAdapter(
            [new DividendPayment(request.Range.From, 99m, Currency.USD, DividendType.Unclassified)]);
        using (var replayHost = BuildHost("durable-complete-nonempty", nonemptyReplayAdapter))
        {
            var replay = await replayHost.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
                .ConfigureAwait(true);

            nonemptyReplayAdapter.Invocations.Should().BeEmpty(
                "the fresh host must use the durable snapshot rather than re-entering the adapter");
            replay.Payload.Should().ContainSingle().Which.Should().Be(payment);
            replay.Coverage.Status.Should().Be(CompletenessStatus.Complete,
                "one annual dividend is complete event-window coverage, not partial daily coverage");
        }

        using (var liveEmptyHost = BuildHost("durable-complete-empty", BuildEmptyWindowAdapter()))
        {
            var liveEmpty = await liveEmptyHost.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
                .ConfigureAwait(true);

            liveEmpty.Payload.Should().BeEmpty();
            liveEmpty.Coverage.Status.Should().Be(CompletenessStatus.Complete);
        }

        var emptyReplayAdapter = BuildAdapter(
            [new DividendPayment(request.Range.From, 99m, Currency.USD, DividendType.Unclassified)]);
        using var replayEmptyHost = BuildHost("durable-complete-empty", emptyReplayAdapter);
        var emptyReplay = await replayEmptyHost.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        emptyReplayAdapter.Invocations.Should().BeEmpty(
            "an authoritative complete-empty answer must survive restart in the durable snapshot tier");
        emptyReplay.Payload.Should().BeEmpty();
        emptyReplay.Coverage.Status.Should().Be(CompletenessStatus.Complete);

        var partialAdapter = new TestDividendAdapter(
            "partial-vendor",
            priority: 100,
            requested => new DataEnvelope<IReadOnlyList<DividendPayment>>(
                [payment],
                new DataCoverage(2, 1, 1, 0.5m),
                [],
                [new DataProvenance(
                    new ProviderCode("partial-vendor"), requested.DatasetKey, LicenseType.Free,
                    RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UtcNow,
                    "https://partial.vendor.test/dgro")]));
        using (var livePartialHost = BuildHost("durable-partial-nonempty", partialAdapter))
        {
            var livePartial = await livePartialHost.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
                .ConfigureAwait(true);

            livePartial.Coverage.Status.Should().Be(CompletenessStatus.Partial);
        }

        var partialReplayAdapter = BuildAdapter(
            [new DividendPayment(request.Range.From, 99m, Currency.USD, DividendType.Unclassified)]);
        using var partialReplayHost = BuildHost("durable-partial-nonempty", partialReplayAdapter);
        var partialReplay = await partialReplayHost.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        partialReplayAdapter.Invocations.Should().BeEmpty(
            "an accepted nonempty Partial response is durable and must replay without adapter entry");
        partialReplay.Coverage.Status.Should().Be(CompletenessStatus.Partial,
            "durable replay must preserve the adapter's accepted completeness classification");
        partialReplay.Coverage.Should().Be(new DataCoverage(2, 1, 1, 0.5m));
    }

    [Fact]
    public async Task Time_series_empty_dividend_window_preserves_complete_coverage()
    {
        var adapter = BuildEmptyWindowAdapter();
        using var sp = BuildHost("time-series-empty", adapter, useTimeSeriesStore: true);
        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var live = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(VymRequest())
            .ConfigureAwait(true);
        var cached = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(VymRequest())
            .ConfigureAwait(true);

        adapter.Invocations.Should().ContainSingle("the empty window is fetched once, then cached");
        live.Payload.Should().BeEmpty();
        live.Coverage.Status.Should().Be(CompletenessStatus.Complete,
            "the adapter successfully established that the event-sparse window contains no payments");
        cached.Coverage.Status.Should().Be(CompletenessStatus.Complete,
            "the cached replay must preserve the accepted event-sparse coverage");
    }

    [Fact]
    public async Task Time_series_foreign_venue_with_no_handling_adapter_returns_an_issue_envelope()
    {
        var adapter = new RejectingDividendAdapter();
        using var sp = BuildHost("time-series-no-handler", adapter, useTimeSeriesStore: true);
        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = new DividendHistoryRequest(
            [new Symbol("SCHD")],
            new DateRange(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)))
        {
            Exchange = ExchangeCode.XTSE,
        };
        DataEnvelope<IReadOnlyList<DividendPayment>>? result = null;

        Func<Task> execute = async () =>
            result = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
                .ConfigureAwait(true);

        await execute.Should().NotThrowAsync<InvalidOperationException>(
            "a valid request that no adapter handles is an ordinary issue-bearing empty result")
            .ConfigureAwait(true);
        result.Should().NotBeNull();
        result!.Payload.Should().BeEmpty();
        result.Issues.Should().ContainSingle(issue => issue.Code == IssueCode.PartitionFailed);
        adapter.FetchCount.Should().Be(0, "CanHandle=false must prevent provider dispatch");
    }

    [Fact]
    public async Task Time_series_live_provenance_preserves_the_selected_adapter_and_cache_rewrites_only_retrieval_fields()
    {
        var adapter = BuildAdapter(s_schdPayments);
        using var sp = BuildHost("time-series-provenance", adapter, useTimeSeriesStore: true);
        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var live = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(SchdRequest())
            .ConfigureAwait(true);
        var cached = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(SchdRequest())
            .ConfigureAwait(true);

        live.Provenance.Should().ContainSingle();
        live.Provenance[0].Provider.Should().Be(new ProviderCode("fake-vendor"));
        live.Provenance[0].RetrievalMode.Should().Be(RetrievalMode.Live);
        live.Provenance[0].SourceUrl.Should().Be("https://fake.vendor.test/schd");

        cached.Provenance.Should().ContainSingle();
        cached.Provenance[0].Provider.Should().Be(new ProviderCode("fake-vendor"));
        cached.Provenance[0].RetrievalMode.Should().Be(RetrievalMode.Cache);
        cached.Provenance[0].SourceUrl.Should().BeNull();
    }

    [Fact]
    public async Task Time_series_restart_recovers_the_selected_provider_before_deciding_to_dispatch()
    {
        var request = new DividendHistoryRequest(
            [new Symbol("SCHD")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));
        var adapter = new TestDividendAdapter(
            "selected-vendor",
            100,
            _ => DividendEnvelope(
                [Payment(new DateOnly(2024, 1, 1)), Payment(new DateOnly(2024, 1, 2))],
                new DataCoverage(2, 2, 0, 1m),
                "selected-vendor"));

        using (var writer = BuildHost("selected-provider-restart", adapter, useTimeSeriesStore: true))
        {
            var live = await writer.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
                .ConfigureAwait(true);
            live.Payload.Should().HaveCount(2);
            adapter.Invocations.Should().ContainSingle();
        }

        using var reader = BuildHost("selected-provider-restart", useTimeSeriesStore: true);
        var replay = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        replay.Payload.Should().HaveCount(2);
        replay.Issues.Should().BeEmpty(
            "the rebuilt host must recover the selected provider key before deciding live dispatch is needed");
    }

    [Fact]
    public async Task Time_series_restart_does_not_apply_one_logical_request_provider_to_another()
    {
        var schd = new DividendHistoryRequest(
            [new Symbol("SCHD")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));
        var vym = schd with { Symbols = [new Symbol("VYM")] };
        var adapter = new TestDividendAdapter(
            "selected-vendor",
            100,
            _ => DividendEnvelope(
                [Payment(new DateOnly(2024, 1, 1)), Payment(new DateOnly(2024, 1, 2))],
                new DataCoverage(2, 2, 0, 1m),
                "selected-vendor"));

        using (var writer = BuildHost("selected-provider-isolation", adapter, useTimeSeriesStore: true))
        {
            _ = await writer.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(schd)
                .ConfigureAwait(true);
        }

        using var reader = BuildHost("selected-provider-isolation", useTimeSeriesStore: true);
        var unrelated = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(vym)
            .ConfigureAwait(true);

        unrelated.Payload.Should().BeEmpty();
        unrelated.Issues.Should().ContainSingle(issue => issue.Code == IssueCode.PartitionFailed,
            "VYM must not inherit SCHD's selected physical provider key or cached records");
    }

    [Fact]
    public async Task Time_series_restart_recovers_complete_known_empty_dividend_spans()
    {
        var request = new DividendHistoryRequest(
            [new Symbol("VYM")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));
        var adapter = new TestDividendAdapter(
            "selected-vendor",
            100,
            _ => DividendEnvelope(
                [Payment(new DateOnly(2024, 1, 1))],
                new DataCoverage(2, 1, 0, 1m),
                "selected-vendor"));

        using (var writer = BuildHost("known-empty-restart", adapter, useTimeSeriesStore: true))
        {
            _ = await writer.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
                .ConfigureAwait(true);
            adapter.Invocations.Should().ContainSingle();
        }

        using var reader = BuildHost("known-empty-restart", useTimeSeriesStore: true);
        var replay = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        replay.Payload.Should().ContainSingle(payment => payment.ExDate == new DateOnly(2024, 1, 1));
        replay.Coverage.Status.Should().Be(CompletenessStatus.Complete,
            "the second date was durably established as known-empty by the complete source response");
        replay.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Time_series_known_empty_range_from_one_provider_does_not_cover_another_provider()
    {
        var first = new DateOnly(2025, 2, 10);
        var emptyDay = first.AddDays(1);
        var tailDay = first.AddDays(2);
        var original = new DividendHistoryRequest(
            [new Symbol("VYM")], new DateRange(first, emptyDay));
        var providerA = new RangeAwareDividendAdapter(
            "empty-provider-a", 100,
            request => request.Range.To <= emptyDay,
            _ => DividendEnvelope([Payment(first, 1m)], new DataCoverage(2, 1, 0, 1m), "empty-provider-a"));
        var providerB = new TestDividendAdapter(
            "populated-provider-b", 10,
            request => DividendEnvelope(
                [Payment(request.Range.From, 2m)], new DataCoverage(1, 1, 0, 1m), "populated-provider-b"));

        using (var writer = BuildHost(
            "known-empty-provider-isolation", providerA, useTimeSeriesStore: true,
            configureServices: services => services.AddSingleton<
                IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(providerB)))
        {
            var pipeline = writer.GetRequiredService<IDataPipeline>();
            var initial = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(original)
                .ConfigureAwait(true);
            initial.Payload.Should().ContainSingle(payment => payment.ExDate == first);
            initial.Coverage.Status.Should().Be(CompletenessStatus.Complete);

            // Selecting B for a disjoint tail must not give B ownership of A's known-empty day.
            _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(
                original with { Range = new DateRange(tailDay, tailDay) }).ConfigureAwait(true);
            providerA.Invocations.Should().ContainSingle();
            providerB.Invocations.Should().ContainSingle().Which.Range.Should()
                .Be(new DateRange(tailDay, tailDay));
        }

        var restartedB = new TestDividendAdapter(
            "populated-provider-b", 10,
            request => DividendEnvelope(
                [Payment(request.Range.From, 2m)], new DataCoverage(1, 1, 0, 1m), "populated-provider-b"));
        using var reader = BuildHost(
            "known-empty-provider-isolation", restartedB, useTimeSeriesStore: true);
        var replay = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(original).ConfigureAwait(true);

        restartedB.Invocations.Should().ContainSingle().Which.Range.Should()
            .Be(new DateRange(emptyDay, emptyDay), "provider A's absence is not provider B's coverage");
        replay.Payload.Should().HaveCount(2)
            .And.Contain(payment => payment.ExDate == emptyDay && payment.Amount == 2m);
        replay.Coverage.Status.Should().Be(CompletenessStatus.Complete);
        replay.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Time_series_durable_provider_attribution_survives_selected_provider_index_expiry()
    {
        var request = new DividendHistoryRequest(
            [new Symbol("SCHD")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));
        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero),
        };
        var adapter = new TestDividendAdapter(
            "selected-vendor",
            100,
            _ => DividendEnvelope(
                [Payment(new DateOnly(2024, 1, 1)), Payment(new DateOnly(2024, 1, 2))],
                new DataCoverage(2, 2, 0, 1m),
                "selected-vendor"));

        using var sp = BuildHost(
            "selected-provider-index-expiry",
            adapter,
            useTimeSeriesStore: true,
            configureServices: services => services.AddSingleton<IClock>(clock),
            configurePipeline: options =>
            {
                options.L1CacheTtl = TimeSpan.Zero;
                options.TimeSeriesRecoveryIndexTtl = TimeSpan.FromMinutes(1);
            });
        var pipeline = sp.GetRequiredService<IDataPipeline>();
        _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request).ConfigureAwait(true);

        clock.UtcNow += TimeSpan.FromMinutes(2);
        var replay = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        adapter.Invocations.Should().ContainSingle(
            "expiry of the bounded selected-provider accelerator must reload the durable attribution");
        replay.Payload.Should().HaveCount(2);
    }

    [Fact]
    public async Task Time_series_durable_known_empty_span_survives_attempted_range_index_expiry()
    {
        var request = new DividendHistoryRequest(
            [new Symbol("VYM")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));
        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero),
        };
        var adapter = new TestDividendAdapter(
            "selected-vendor",
            100,
            _ => DividendEnvelope(
                [Payment(new DateOnly(2024, 1, 1))],
                new DataCoverage(2, 1, 0, 1m),
                "selected-vendor"));

        using var sp = BuildHost(
            "attempted-range-index-expiry",
            adapter,
            useTimeSeriesStore: true,
            configureServices: services => services.AddSingleton<IClock>(clock),
            configurePipeline: options =>
            {
                options.L1CacheTtl = TimeSpan.Zero;
                options.TimeSeriesRecoveryIndexTtl = TimeSpan.FromMinutes(1);
            });
        var pipeline = sp.GetRequiredService<IDataPipeline>();
        _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request).ConfigureAwait(true);

        clock.UtcNow += TimeSpan.FromMinutes(2);
        var replay = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        adapter.Invocations.Should().ContainSingle(
            "expiry of the bounded attempted-range accelerator must reload the durable known-empty span");
        replay.Coverage.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Fact]
    public async Task Time_series_evicted_nondurable_attempt_permits_a_safe_refetch()
    {
        var request = new DividendHistoryRequest(
            [new Symbol("VYM")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));
        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero),
        };
        var index = new ObservingAttemptedRangeIndex(1, TimeSpan.FromMinutes(1), clock);
        index.SeedNextLookup(request.Range.From, request.Range.To);
        var adapter = new TestDividendAdapter(
            "selected-vendor",
            100,
            _ => DividendEnvelope([], new DataCoverage(0, 0, 0, 1m), "selected-vendor"));

        using var sp = BuildHost(
            "attempted-range-nondurable-eviction",
            adapter,
            useTimeSeriesStore: true,
            configureServices: services =>
            {
                services.AddSingleton<IClock>(clock);
                services.AddSingleton<IAttemptedRangeIndex>(index);
            },
            configurePipeline: options =>
            {
                options.L1CacheTtl = TimeSpan.Zero;
                options.TimeSeriesRecoveryIndexCapacity = 1;
                options.TimeSeriesRecoveryIndexTtl = TimeSpan.FromMinutes(1);
            });
        var pipeline = sp.GetRequiredService<IDataPipeline>();

        _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        adapter.Invocations.Should().BeEmpty(
            "the seeded accelerator entry is deliberately non-durable and initially suppresses dispatch");
        index.Lookups.Should().ContainSingle();
        var requestIdentity = index.Lookups.Single();

        index.RecordAttempt("capacity-evictor", request.Range.From, request.Range.To);
        index.GetAttemptedRanges(requestIdentity).Should().BeEmpty(
            "exceeding the configured capacity must evict the older request identity");
        clock.UtcNow += TimeSpan.FromMinutes(2);
        index.GetAttemptedRanges("capacity-evictor").Should().BeEmpty(
            "advancing the deterministic clock past TTL must expire the remaining identity");

        var refetched = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        adapter.Invocations.Should().ContainSingle(
            "without durable metadata, accelerator eviction must permit a safe provider refetch");
        refetched.Coverage.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Fact]
    public async Task Time_series_durable_known_empty_span_survives_capacity_eviction_and_rebuilt_host()
    {
        var range = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2));
        var durableRequest = new DividendHistoryRequest([new Symbol("VYM")], range);
        var evictingRequest = new DividendHistoryRequest([new Symbol("SCHD")], range);
        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero),
        };
        var index = new ObservingAttemptedRangeIndex(1, TimeSpan.FromMinutes(1), clock);
        var adapter = new TestDividendAdapter(
            "selected-vendor",
            100,
            request => DividendEnvelope(
                [Payment(request.Range.From)],
                new DataCoverage(2, 1, 0, 1m),
                "selected-vendor"));

        using (var writer = BuildHost(
            "attempted-range-capacity-restart",
            adapter,
            useTimeSeriesStore: true,
            configureServices: services =>
            {
                services.AddSingleton<IClock>(clock);
                services.AddSingleton<IAttemptedRangeIndex>(index);
            },
            configurePipeline: options =>
            {
                options.L1CacheTtl = TimeSpan.Zero;
                options.TimeSeriesRecoveryIndexCapacity = 1;
                options.TimeSeriesRecoveryIndexTtl = TimeSpan.FromMinutes(1);
            }))
        {
            var pipeline = writer.GetRequiredService<IDataPipeline>();
            _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(durableRequest)
                .ConfigureAwait(true);
            index.RecordedIdentities.Should().ContainSingle();
            var durableIdentity = index.RecordedIdentities.Single();

            _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(evictingRequest)
                .ConfigureAwait(true);
            index.GetAttemptedRanges(durableIdentity).Should().BeEmpty(
                "the second physical series must evict the first from the capacity-one accelerator");

            clock.UtcNow += TimeSpan.FromMinutes(2);
            index.GetAttemptedRanges(index.RecordedIdentities[^1]).Should().BeEmpty(
                "the deterministic expiry bound remains active after capacity eviction");
        }

        var replayAdapter = new TestDividendAdapter(
            "selected-vendor",
            100,
            _ => DividendEnvelope(
                [Payment(range.To, 99m)],
                new DataCoverage(2, 1, 0, 1m),
                "selected-vendor"));
        using var reader = BuildHost(
            "attempted-range-capacity-restart",
            replayAdapter,
            useTimeSeriesStore: true,
            configureServices: services => services.AddSingleton<IClock>(clock),
            configurePipeline: options =>
            {
                options.L1CacheTtl = TimeSpan.Zero;
                options.TimeSeriesRecoveryIndexCapacity = 1;
                options.TimeSeriesRecoveryIndexTtl = TimeSpan.FromMinutes(1);
            });

        var replay = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(durableRequest)
            .ConfigureAwait(true);

        replayAdapter.Invocations.Should().BeEmpty(
            "a rebuilt host must reload durable known-empty truth after accelerator eviction");
        replay.Payload.Should().ContainSingle(payment => payment.ExDate == range.From);
        replay.Coverage.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Fact]
    public async Task Time_series_fallback_tail_reconciles_records_across_provider_keys_after_restart()
    {
        var day1 = new DateOnly(2024, 1, 1);
        var day2 = new DateOnly(2024, 1, 2);
        var day3 = new DateOnly(2024, 1, 3);
        var initial = new DividendHistoryRequest([new Symbol("SCHD")], new DateRange(day1, day2));
        var extended = initial with { Range = new DateRange(day1, day3) };

        var primary = new RangeAwareDividendAdapter(
            "provider-a",
            100,
            request => request.Range.To <= day2,
            _ => DividendEnvelope(
                [Payment(day1, 11m), Payment(day2, 12m)],
                new DataCoverage(2, 2, 0, 1m),
                "provider-a"));
        var fallback = new RangeAwareDividendAdapter(
            "provider-b",
            50,
            _ => true,
            _ => DividendEnvelope(
                [Payment(day2, 22m), Payment(day3, 13m)],
                new DataCoverage(2, 2, 0, 1m),
                "provider-b"));

        using (var writer = BuildHost(
            "provider-tail-reconciliation",
            useTimeSeriesStore: true,
            configureServices: services =>
            {
                services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(primary);
                services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(fallback);
            }))
        {
            var pipeline = writer.GetRequiredService<IDataPipeline>();
            _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(initial).ConfigureAwait(true);
            var merged = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(extended)
                .ConfigureAwait(true);

            merged.Payload.Select(payment => payment.ExDate).Should().Equal(day1, day2, day3);
            merged.Payload.Single(payment => payment.ExDate == day2).Amount.Should().Be(12m,
                "the canonical physical-key order must resolve duplicate dates independently of fallback timing");
            merged.Coverage.Status.Should().Be(CompletenessStatus.Complete);
        }

        using var reader = BuildHost("provider-tail-reconciliation", useTimeSeriesStore: true);
        var replay = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(extended)
            .ConfigureAwait(true);

        replay.Payload.Select(payment => payment.ExDate).Should().Equal(day1, day2, day3);
        replay.Payload.Single(payment => payment.ExDate == day2).Amount.Should().Be(12m,
            "restart must apply the same deterministic duplicate-date policy");
        replay.Coverage.Status.Should().Be(CompletenessStatus.Complete);
        replay.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task Time_series_partial_coverage_is_retried_instead_of_becoming_complete_by_attempt_clamping()
    {
        var day1 = new DateOnly(2024, 1, 1);
        var day2 = new DateOnly(2024, 1, 2);
        var partialAdapter = new TestDividendAdapter(
            "partial-then-complete",
            100,
            _ => DividendEnvelope(
                [Payment(day1)],
                new DataCoverage(2, 1, 1, 0.5m),
                "partial-then-complete"));
        var original = new DividendHistoryRequest(
            [new Symbol("SCHD")],
            new DateRange(day1, day2));
        var unresolvedDate = original with { Range = new DateRange(day2, day2) };

        using (var writer = BuildHost("partial-coverage-retry", partialAdapter, useTimeSeriesStore: true))
        {
            var initial = await writer.GetRequiredService<IDataPipeline>()
                .ExecuteAsync<DividendHistoryRequest, DividendPayment>(original)
                .ConfigureAwait(true);
            initial.Coverage.Status.Should().Be(CompletenessStatus.Partial);
        }

        var resolvingAdapter = new TestDividendAdapter(
            "partial-then-complete",
            100,
            _ => DividendEnvelope(
                [Payment(day2)],
                new DataCoverage(1, 1, 0, 1m),
                "partial-then-complete"));
        using var reader = BuildHost(
            "partial-coverage-retry",
            resolvingAdapter,
            useTimeSeriesStore: true);
        var resolved = await reader.GetRequiredService<IDataPipeline>()
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(unresolvedDate)
            .ConfigureAwait(true);

        partialAdapter.Invocations.Should().ContainSingle().Which.Range.Should().Be(original.Range);
        resolvingAdapter.Invocations.Should().ContainSingle(
            "a durable partial response must not suppress the unresolved date after restart");
        resolvingAdapter.Invocations[0].Range.Should().Be(unresolvedDate.Range);
        resolved.Payload.Should().ContainSingle(payment => payment.ExDate == day2);
        resolved.Coverage.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Fact]
    public async Task Time_series_staleness_handles_an_existing_gap_at_DateOnly_MinValue()
    {
        var first = new DateOnly(DateOnly.MinValue.Year, 1, 1);
        var second = first.AddDays(1);
        var third = first.AddDays(2);
        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
        var adapter = new TestDividendAdapter(
            "min-date-staleness",
            100,
            request => DividendEnvelope(
                [Payment(first)],
                new DataCoverage(
                    request.Range.From == first && request.Range.To == second ? 2 : 3,
                    1,
                    request.Range.From == first && request.Range.To == second ? 1 : 2,
                    request.Range.From == first && request.Range.To == second ? 0.5m : 0.333333m),
                "min-date-staleness"));
        var initial = new DividendHistoryRequest([new Symbol("VYM")], new DateRange(first, second));
        var extended = initial with { Range = new DateRange(first, third) };

        using var sp = BuildHost(
            "min-date-staleness",
            adapter,
            useTimeSeriesStore: true,
            configureServices: services => services.AddSingleton<IClock>(clock),
            configurePipeline: options => options.TimeSeriesStalenessMaxAge = TimeSpan.FromDays(1));
        var pipeline = sp.GetRequiredService<IDataPipeline>();
        _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(initial).ConfigureAwait(true);

        Func<Task> act = async () => _ = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(extended)
            .ConfigureAwait(true);

        await act.Should().NotThrowAsync<ArgumentOutOfRangeException>(
            "minimum-date stale-tail planning must not subtract one day from DateOnly.MinValue")
            .ConfigureAwait(true);
        adapter.Invocations.Should().HaveCount(2);
    }

    [Fact]
    public async Task Attempted_ranges_are_isolated_by_request_family_for_the_same_ticker()
    {
        var dividendAdapter = BuildEmptyWindowAdapter();
        var priceAdapter = new FakeDataSourceAdapter<PriceHistoryRequest, Bar>(
            new ProviderCode("price-vendor"),
            request =>
            {
                var bar = new Bar(request.Range.From, 100m, 101m, 99m, 100m, 100m, 1_000);
                var provenance = new DataProvenance(
                    new ProviderCode("price-vendor"), request.DatasetKey, LicenseType.Free,
                    RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UtcNow,
                    "https://price.vendor.test/vym");
                return new DataEnvelope<IReadOnlyList<Bar>>(
                    [bar], new DataCoverage(1, 1, 0, 1.0m), [], [provenance]);
            });

        using var sp = BuildHost(
            "attempted-family-isolation",
            dividendAdapter,
            useTimeSeriesStore: true,
            configureServices: services =>
                services.AddSingleton<IDataSourceAdapter<PriceHistoryRequest, Bar>>(priceAdapter));
        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var dividendRequest = VymRequest();
        var priceRequest = new PriceHistoryRequest(dividendRequest.Symbols, dividendRequest.Range);
        _ = await pipeline.ExecuteAsync<PriceHistoryRequest, Bar>(priceRequest).ConfigureAwait(true);
        var dividends = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(dividendRequest)
            .ConfigureAwait(true);

        priceAdapter.Invocations.Should().ContainSingle();
        dividendAdapter.Invocations.Should().ContainSingle(
            "a price attempt for VYM must not suppress a dividend attempt for VYM");
        dividends.Coverage.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Fact]
    public async Task Attempted_range_clamping_handles_DateOnly_MaxValue_without_incrementing_past_it()
    {
        var adapter = BuildEmptyWindowAdapter();
        using var sp = BuildHost("attempted-max-date", adapter, useTimeSeriesStore: true);
        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var first = new DividendHistoryRequest(
            [new Symbol("VYM")],
            new DateRange(new DateOnly(2024, 1, 1), DateOnly.MaxValue));
        var overlapping = first with
        {
            Range = new DateRange(new DateOnly(2025, 1, 1), DateOnly.MaxValue),
        };

        _ = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(first)
            .ConfigureAwait(true);
        Func<Task> act = async () => _ = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(overlapping)
            .ConfigureAwait(true);

        await act.Should().NotThrowAsync<ArgumentOutOfRangeException>(
            "a covering attempted range ending at DateOnly.MaxValue has no representable successor")
            .ConfigureAwait(true);
        adapter.Invocations.Should().ContainSingle(
            "the overlapping range is fully covered by the successful first attempt");
    }

    [Fact]
    public async Task Staleness_does_not_redispatch_an_already_confirmed_empty_dividend_tail()
    {
        var request = new DividendHistoryRequest(
            [new Symbol("DGRO")],
            new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)));
        var adapter = BuildAdapter(
            [new DividendPayment(
                new DateOnly(2024, 3, 22), 0.31m, Currency.USD, DividendType.Unclassified)]);
        var clock = new FakeClock
        {
            UtcNow = new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero),
        };
        using var sp = BuildHost(
            "stale-confirmed-empty-tail",
            adapter,
            useTimeSeriesStore: true,
            configureServices: services => services.AddSingleton<IClock>(clock),
            configurePipeline: options =>
            {
                options.L1CacheTtl = TimeSpan.Zero;
                options.TimeSeriesStalenessMaxAge = TimeSpan.FromDays(7);
            });
        var pipeline = sp.GetRequiredService<IDataPipeline>();

        var live = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);
        var replay = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        adapter.Invocations.Should().ContainSingle(
            "the first full-window attempt already confirmed that the March-to-December tail "
            + "contains no additional dividend events");
        adapter.Invocations[0].Range.Should().Be(request.Range);
        live.Coverage.Status.Should().Be(CompletenessStatus.Complete);
        replay.Coverage.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Fact]
    public async Task Time_series_persists_dividends_under_the_fallback_selected_provider_key()
    {
        var rejected = new TestDividendAdapter(
            "aaa-rejected",
            priority: 200,
            request => new DataEnvelope<IReadOnlyList<DividendPayment>>(
                [], new DataCoverage(1, 0, 1, 0m), [],
                [new DataProvenance(
                    new ProviderCode("aaa-rejected"), request.DatasetKey, LicenseType.Free,
                    RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UtcNow,
                    "https://rejected.vendor.test/schd")]));
        var selected = new TestDividendAdapter(
            "selected-vendor",
            priority: 100,
            request => new DataEnvelope<IReadOnlyList<DividendPayment>>(
                s_schdPayments, new DataCoverage(2, 2, 0, 1m), [],
                [new DataProvenance(
                    new ProviderCode("selected-vendor"), request.DatasetKey, LicenseType.Commercial,
                    RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UtcNow,
                    "https://selected.vendor.test/schd")]));

        using var sp = BuildHost(
            "selected-provider-key",
            useTimeSeriesStore: true,
            configureServices: services =>
            {
                services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(rejected);
                services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(selected);
            });
        var pipeline = sp.GetRequiredService<IDataPipeline>();

        _ = await pipeline
            .ExecuteAsync<DividendHistoryRequest, DividendPayment>(SchdRequest())
            .ConfigureAwait(true);

        var store = sp.GetRequiredService<ITimeSeriesStoreRegistry>()
            .Resolve<DividendPaymentDatasetKey, DividendPayment>();
        store.Should().NotBeNull();
        var persisted = new List<DividendPayment>();
        await foreach (var payment in store!
            .GetRangeAsync(
                new DividendPaymentDatasetKey("selected-vendor", "SCHD"),
                SchdRequest().Range.From,
                SchdRequest().Range.To)
            .ConfigureAwait(true))
        {
            persisted.Add(payment);
        }

        rejected.Invocations.Should().ContainSingle("the higher-priority response is rejected");
        selected.Invocations.Should().ContainSingle("the lower-priority accepted response wins fallback");
        persisted.Should().BeEquivalentTo(s_schdPayments,
            "the selected adapter identity must determine the physical dividend dataset key");
    }

    [Fact]
    public async Task ExecuteAsync_falls_back_after_a_partial_empty_dividend_response()
    {
        var partialEmpty = new TestDividendAdapter(
            "partial-empty",
            priority: 200,
            request => new DataEnvelope<IReadOnlyList<DividendPayment>>(
                [], new DataCoverage(2, 1, 1, 0.5m), [],
                [new DataProvenance(
                    new ProviderCode("partial-empty"), request.DatasetKey, LicenseType.Free,
                    RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UtcNow,
                    "https://partial.vendor.test/dgro")]));
        DividendPayment heldOutPayment = new(
            new DateOnly(2025, 9, 26), 0.3421m, Currency.USD, DividendType.Unclassified);
        var complete = new TestDividendAdapter(
            "complete",
            priority: 100,
            request => new DataEnvelope<IReadOnlyList<DividendPayment>>(
                [heldOutPayment], new DataCoverage(1, 1, 0, 1m), [],
                [new DataProvenance(
                    new ProviderCode("complete"), request.DatasetKey, LicenseType.Commercial,
                    RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UtcNow,
                    "https://complete.vendor.test/dgro")]));

        using var sp = BuildHost(
            "partial-empty-fallback",
            configureServices: services =>
            {
                services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(partialEmpty);
                services.AddSingleton<IDataSourceAdapter<DividendHistoryRequest, DividendPayment>>(complete);
            });
        var pipeline = sp.GetRequiredService<IDataPipeline>();
        var request = new DividendHistoryRequest(
            [new Symbol("DGRO")],
            new DateRange(new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)));

        var result = await pipeline.ExecuteAsync<DividendHistoryRequest, DividendPayment>(request)
            .ConfigureAwait(true);

        partialEmpty.Invocations.Should().ContainSingle();
        complete.Invocations.Should().ContainSingle("Partial empty coverage must not stop fallback");
        result.Payload.Should().ContainSingle().Which.Should().Be(heldOutPayment);
    }
}
