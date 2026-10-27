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
using Boutquin.MarketData.Abstractions.Calendars;
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Enums;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Observability;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Cache.Memory;
using Boutquin.MarketData.Cache.Policies;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.MarketData.Cache.WriteThrough;
using Boutquin.MarketData.Calendars;
using Boutquin.MarketData.Normalization.Normalizers;
using Boutquin.MarketData.Orchestration.Coverage;
using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.MarketData.Orchestration.Snapshots;
using Boutquin.MarketData.Storage.Checkpointing;
using Boutquin.MarketData.Storage.Events;
using Boutquin.MarketData.Storage.RawDocuments;
using Boutquin.MarketData.Storage.Serializers;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.MarketData.Transport.Http;
using Boutquin.Storage.Domain.Enums;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.Events;
using Boutquin.Storage.Domain.Interfaces.Schemas;
using Boutquin.Storage.Domain.Interfaces.Streaming;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.Interfaces.TimeSeries.Columnar;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;
using Boutquin.Storage.Infrastructure.Events;
using Boutquin.Storage.Infrastructure.Schemas;
using Boutquin.Storage.Infrastructure.Streaming.Checkpointing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Boutquin.MarketData.DependencyInjection;

/// <summary>
/// Registers the MarketData kernel services — clock, transport, cache, snapshot storage,
/// raw document storage, checkpointing, event logging, and schema infrastructure —
/// in one call. Domain DI extensions (Market, Trading) should call
/// this first, then register their own adapters and builders on top.
/// </summary>
/// <remarks>
/// All kernel services are registered as singletons. Each registration uses
/// <c>TryAdd*</c> methods so that callers can pre-register custom implementations
/// (e.g., a fake clock for tests) and this method will not overwrite them.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the MarketData shared kernel services with default implementations.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="snapshotDirectory">
    /// Root directory for <see cref="FileSnapshotStore"/>. Defaults to
    /// <c>.marketdata-snapshots</c> under <see cref="AppContext.BaseDirectory"/>.
    /// </param>
    /// <param name="rawDocumentDirectory">
    /// Root directory for <see cref="FileRawDocumentStore"/>. Defaults to
    /// <c>.marketdata-raw</c> under <see cref="AppContext.BaseDirectory"/>.
    /// </param>
    /// <param name="checkpointDirectory">
    /// Root directory for <see cref="FileCheckpointStore"/>. Defaults to
    /// <c>.marketdata-checkpoints</c> under <see cref="AppContext.BaseDirectory"/>.
    /// </param>
    /// <param name="eventLogDirectory">
    /// Root directory for <see cref="AppendOnlyEventLog{TEvent}"/>. Defaults to
    /// <c>.marketdata-events</c> under <see cref="AppContext.BaseDirectory"/>.
    /// </param>
    /// <param name="timeSeriesDirectory">
    /// Root directory for the columnar time-series store segments. Defaults to
    /// <c>.marketdata-timeseries</c> under <see cref="AppContext.BaseDirectory"/>. One
    /// sub-directory per record type is created beneath it.
    /// </param>
    /// <param name="configureTransport">
    /// Optional callback to configure <see cref="TransportOptions"/> (retry, rate limiting, timeouts).
    /// </param>
    /// <param name="configurePipeline">
    /// Optional callback to configure <see cref="DataPipelineOptions"/> (L1 TTL, failure behavior).
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddMarketDataKernel(
        this IServiceCollection services,
        string? snapshotDirectory = null,
        string? rawDocumentDirectory = null,
        string? checkpointDirectory = null,
        string? eventLogDirectory = null,
        string? timeSeriesDirectory = null,
        Action<TransportOptions>? configureTransport = null,
        Action<DataPipelineOptions>? configurePipeline = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Clock
        services.TryAddSingleton<IClock, SystemClock>();

        // Transport (with Polly resilience)
        services.TryAddSingleton<IHttpDataTransport>(sp =>
        {
            var opts = new TransportOptions();
            configureTransport?.Invoke(opts);
            var logger = sp.GetService<ILoggerFactory>()
                ?.CreateLogger<ResilientHttpDataTransport>();
            return new ResilientHttpDataTransport(options: opts, logger: logger, ownsClient: true);
        });

        // L1 cache
        services.TryAddSingleton<ILocalCache, ExactOnceMemoryCache>();

        // Snapshot store (canonical, post-normalization)
        var snapDir = snapshotDirectory
            ?? Path.Combine(AppContext.BaseDirectory, ".marketdata-snapshots");
        services.TryAddSingleton<ISnapshotStore>(
            _ => new FileSnapshotStore(snapDir));

        // Raw document store (pre-normalization payloads)
        var rawDir = rawDocumentDirectory
            ?? Path.Combine(AppContext.BaseDirectory, ".marketdata-raw");
        services.TryAddSingleton<IRawDocumentStore>(
            _ => new FileRawDocumentStore(rawDir));

        // Checkpointing
        var cpDir = checkpointDirectory
            ?? Path.Combine(AppContext.BaseDirectory, ".marketdata-checkpoints");
        services.TryAddSingleton<ICheckpointStore>(
            _ => new FileCheckpointStore(cpDir));
        services.TryAddSingleton<IngestionCheckpointStore>();

        // Event log. AppendOnlyEventLog creates the parent directory of the file path during
        // construction, so DI does not (and must not) touch System.IO.Directory directly — that
        // would leak file-system access outside the Storage package. Storage 2.0 moved construction
        // behind the async factory AppendOnlyEventLog<T>.CreateAsync (it replays the existing log on
        // open); the singleton factory blocks on it at composition-root time, which is acceptable for
        // a one-time eager singleton build.
        var logDir = eventLogDirectory
            ?? Path.Combine(AppContext.BaseDirectory, ".marketdata-events");
        services.TryAddSingleton<IEventLog<IngestionEvent>>(
            _ => AppendOnlyEventLog<IngestionEvent>
                .CreateAsync(Path.Combine(logDir, "ingestion.log"))
                .GetAwaiter()
                .GetResult());
        services.TryAddSingleton<IngestionEventLog>();

        // Schema infrastructure. Storage 2.0 split the former FieldLevelCompatibilityChecker into
        // format-specific checkers; MarketData's records use the tagged wire, so the tagged-wire
        // checker is the correct compatibility policy.
        services.TryAddSingleton<ISchemaCompatibilityChecker, TaggedWireCompatibilityChecker>();
        services.TryAddSingleton<ISchemaRegistry>(sp =>
            new InMemorySchemaRegistry(sp.GetRequiredService<ISchemaCompatibilityChecker>()));

        // Pipeline options. AddOptions() is idempotent and guarantees IOptions<DataPipelineOptions>
        // resolves even when NEITHER the host nor this call supplies a delegate (DataPipeline takes
        // it as a constructor dependency). The kernel's own configurePipeline is then registered
        // UNCONDITIONALLY so it always joins the IConfigureOptions<DataPipelineOptions> chain that
        // IOptions<T> applies at runtime.
        //
        // It was previously registered only when NO IConfigureOptions<DataPipelineOptions> existed
        // yet. That predicate keyed on the presence of ANY such descriptor regardless of registrant,
        // so a host that bound the options section before this call — and equally a second
        // AddMarketDataKernel, the shape the guard was written for — had the kernel's own delegate
        // dropped on the floor: its values reached the composition-time locals below but never the
        // IOptions<DataPipelineOptions> instance DataPipeline reads at runtime, with no log and no
        // assertion. Accumulating delegates is the correct behavior here and is not order-ambiguous:
        // IConfigureOptions run in registration order, so the last registrant wins per property,
        // which is exactly what a caller passing configurePipeline expects.
        services.AddOptions();
        if (configurePipeline is not null)
        {
            services.Configure(configurePipeline);
        }

        // Materialize the composition-time option values once, eagerly. They shape the one class of
        // decision that genuinely cannot wait — the store BUILDERS below, which are configured
        // before any IServiceProvider or IOptions<T> exists. (Normalizer registrations were
        // materialized here too until the 2026-07-23 amendment moved them to resolution-time
        // factories; they now read the flag off IOptions and no longer depend on this value.)
        //
        // They are resolved from the SAME registered IConfigureOptions chain IOptions<T> will apply
        // at runtime — in registration order — rather than from a private instance fed only by the
        // configurePipeline parameter. A private instance made the options-binding channel the class
        // documents (services.Configure<DataPipelineOptions>(...), which is also what IConfiguration
        // section binding registers) silently inert for every composition-time value, so the two
        // surfaces reported different configurations. Reading the chain keeps them consistent.
        //
        // Only instance-registered configurers are applied — the two documented channels,
        // Configure(Action<T>) and Configure(IConfiguration), both register a ConfigureNamedOptions
        // INSTANCE. An IConfigureOptions registered by implementation type or factory (the advanced
        // services.ConfigureOptions<T>() shape) needs an IServiceProvider to construct, and none
        // exists during composition; a host using that shape must pass values this loop still reads
        // — the store-shaping options — through configurePipeline instead. It does NOT need to
        // for RegisterDefaultNormalizers, which is read at resolution time.
        var pipelineOptions = new DataPipelineOptions();
        foreach (var descriptor in services)
        {
            if (!descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(IConfigureOptions<DataPipelineOptions>)
                && descriptor.ImplementationInstance is IConfigureOptions<DataPipelineOptions> configure)
            {
                configure.Configure(pipelineOptions);
            }
        }

        ValidateStoreShapingOptions(pipelineOptions);

        // Pipeline orchestration. Registered as a factory rather than by implementation type so the
        // composition notice can be emitted from inside it — see LogNormalizerComposition for
        // why the notice cannot be emitted at composition time. ActivatorUtilities.CreateInstance
        // reproduces the implementation-type registration's constructor SELECTION exactly (DataPipeline
        // has exactly one public constructor, so there is nothing to select between). That is narrower
        // than "the only behavioral difference is the notice": a factory registration is opaque to
        // BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }), whereas an
        // implementation-type registration is statically validated at Build() time. A host that opts
        // into ValidateOnBuild therefore loses that build-time dependency check for IDataPipeline and
        // instead fails at first resolution — under the default BuildServiceProvider() (no
        // ValidateOnBuild) both registration shapes fail at the same point, so there is no behavior
        // change on the default path. Singleton, so the notice fires exactly once per built provider no
        // matter how often IDataPipeline is resolved.
        // The notice derives its branch by RESOLVING the normalizers off the built provider rather
        // than from this call's pipelineOptions flag. TryAddSingleton makes the FIRST kernel call's
        // factory the one that wins, so a captured flag would freeze that call's value while a
        // later call's options delegate still changes what the container resolves: the notice would
        // then report the negation of what the container actually resolves.
        services.TryAddSingleton<IDataPipeline>(sp =>
        {
            LogNormalizerComposition(sp);
            return ActivatorUtilities.CreateInstance<DataPipeline>(sp);
        });
        services.TryAddSingleton<SnapshotStalenessPolicy>();
        services.TryAddSingleton<IBusinessCalendar, WeekendOnlyCalendar>();
        services.TryAddSingleton<CoverageCalculator>();

        // L2 write-through cache for every record type with a normalizer. Missing a record
        // type here silently disables L2 caching for that type — pipelines fetching it fall
        // straight through to the live adapter every call, so we must register every normalized
        // record the kernel ships.
        AddWriteThroughFor<ScalarObservation>(services);
        AddWriteThroughFor<YieldCurveQuote>(services);
        AddWriteThroughFor<FuturesSettlement>(services);
        AddWriteThroughFor<Bar>(services);
        AddWriteThroughFor<FxRate>(services);
        AddWriteThroughFor<InstrumentMetadata>(services);
        AddWriteThroughFor<CorporateAction>(services);
        AddWriteThroughFor<DividendPayment>(services);
        AddWriteThroughFor<FactorObservation>(services);
        AddWriteThroughFor<OptionQuote>(services);

        // Time-series store registry + one live store per dated record type. Columnar records use
        // the columnar engine; Storage 2.0 row-format support also registers the
        // [StorageSerializable]-only FuturesSettlement, OptionQuote, and DividendPayment records.
        // This is what makes the time-series cache tier LIVE: DefaultTimeSeriesStoreRegistry is no
        // longer an empty dictionary — Resolve<,> returns a constructed LsmTimeSeriesStore.
        // The store-shaping options materialized above shape the store BUILDERS configured
        // below. The builders are configured at composition time — capturing
        // the resolved option values before any IServiceProvider/IOptions<T> exists — so the
        // delegate was invoked directly rather than read through IOptions; each engine is then
        // opened lazily from its captured builder when the registry is resolved.

        // Default normalizer registrations (resolution-time factories since
        // 2026-07-23). The normalization stage is dormant in every production composition unless
        // SOMETHING registers an INormalizer<TRecord, TRecord> — DataPipeline resolves it optionally
        // and skips the stage when absent — and nothing did before this flag existed. Opt-in
        // (default false) so no existing consumer's fetched or cached values change silently.
        //
        // RESOLUTION-TIME FACTORY, not an eager block gated on the materialized flag. The original
        // shape read pipelineOptions.RegisterDefaultNormalizers synchronously here, so a value the
        // host supplied AFTER AddMarketDataKernel returned — a later services.Configure, any
        // PostConfigure (which runs after every Configure, so it wins in either order), or
        // services.ConfigureOptions<T>() (registered by type, hence unconstructible before an
        // IServiceProvider exists) — reached IOptions and never reached this block: the host read
        // RegisterDefaultNormalizers = true off its own options instance while ZERO normalizers were
        // registered. Reading the flag inside the factory moves the read downstream of every
        // options channel, so composition and IOptions cannot disagree about the flag's value.
        //
        // Returning null when the flag is off is the opt-out: the descriptor exists but resolves to
        // nothing, which is what GetService already returns for an unregistered service.
        //
        // NULL-FACTORY PRECONDITION: this is legal only because every consumer resolves a
        // normalizer through GetService, which tolerates null. DataPipeline and both executor
        // normalization paths use optional resolution. A
        // consumer written with GetRequiredService<INormalizer<T,T>> would get "No service for type
        // ... has been registered", which is an actively misleading message for a service that IS
        // registered and deliberately inactive. Add such a consumer only alongside a change here.
        //
        // TryAddSingleton, deliberately: a host registration made BEFORE this call wins because
        // TryAdd yields to it, and one made AFTER wins because GetService resolves the last
        // registration — host override in both orders. Per-record opt-out is
        // services.RemoveAll<INormalizer<T, T>>(), not a bespoke exclusion API.
        //
        // SINGLETON PRECONDITION: every shipped normalizer is stateless across Normalize
        // calls — readonly instance fields only, and the one static collection is read-only at use.
        // A future normalizer carrying mutable per-call state (buffer, accumulator, cached last
        // input) is a data race the moment it joins this block: make it stateless, or register it
        // with a non-singleton lifetime and record the asymmetry here. Never add the line and leave
        // the state in place.
        //
        // InstrumentMetadata is absent by design — no normalization semantics are defined for
        // metadata, so it ships no normalizer and the pipeline's null-tolerant resolution handles it
        // exactly as today.
        //
        // Placed OUTSIDE the DefaultTimeSeriesStoresMarker once-guard: TryAddSingleton is idempotent
        // per service type, so a repeated AddMarketDataKernel adds no second descriptor. That claim
        // is proven by a descriptor-count test, not assumed — the marker exists precisely because a
        // repeated kernel call once silently double-opened LSM engines over one folder. Under the
        // factory shape the first call's descriptor is the surviving one, but it carries no captured
        // flag value, so a repeated composition resolves the LAST-WINS options value on both
        // surfaces rather than freezing the first call's.
        services.TryAddSingleton<INormalizer<Bar, Bar>>(
            sp => DefaultNormalizersRequested(sp) ? new BarNormalizer() : null!);
        services.TryAddSingleton<INormalizer<CorporateAction, CorporateAction>>(
            sp => DefaultNormalizersRequested(sp) ? new CorporateActionNormalizer() : null!);
        services.TryAddSingleton<INormalizer<DividendPayment, DividendPayment>>(
            sp => DefaultNormalizersRequested(sp) ? new DividendPaymentNormalizer() : null!);
        services.TryAddSingleton<INormalizer<FactorObservation, FactorObservation>>(
            sp => DefaultNormalizersRequested(sp) ? new FactorObservationNormalizer() : null!);
        services.TryAddSingleton<INormalizer<FuturesSettlement, FuturesSettlement>>(
            sp => DefaultNormalizersRequested(sp) ? new FuturesSettlementNormalizer() : null!);
        services.TryAddSingleton<INormalizer<FxRate, FxRate>>(
            sp => DefaultNormalizersRequested(sp) ? new FxRateNormalizer() : null!);
        services.TryAddSingleton<INormalizer<OptionQuote, OptionQuote>>(
            sp => DefaultNormalizersRequested(sp) ? new OptionQuoteNormalizer() : null!);
        services.TryAddSingleton<INormalizer<ScalarObservation, ScalarObservation>>(
            sp => DefaultNormalizersRequested(sp) ? new ScalarObservationNormalizer() : null!);
        services.TryAddSingleton<INormalizer<YieldCurveQuote, YieldCurveQuote>>(
            sp => DefaultNormalizersRequested(sp) ? new YieldCurveQuoteNormalizer() : null!);

        // Normalizer observability is emitted from the IDataPipeline singleton factory registered above,
        // NOT from here: at this point no IServiceProvider exists, so no ILoggerFactory registered
        // the way a real host registers one (AddLogging(...) — by implementation type) can be
        // resolved. See LogNormalizerComposition.

        // Dividend fallback-acceptance rule. The pipeline resolves a per-record-type
        // IFallbackResponseEvaluator and only falls back to CoverageResponseEvaluator<TRecord>
        // when none is registered, so this closed registration re-decides exactly one record
        // type's empty-payload case and leaves the other nine on the default, untouched.
        //
        // Why dividends need their own rule: the default rejects an empty payload BEFORE it
        // consults coverage, which is correct for a series where every trading day in range owes
        // a point. Dividends are event-sparse — a symbol that distributed nothing in the window
        // legitimately returns zero records — so under the default the ordinary non-payer case
        // exhausts the fallback chain and is reported as a failed partition, indistinguishable
        // from a provider outage. See DividendWindowResponseEvaluator for the full rationale.
        //
        // TryAddSingleton so a host that wants a stricter dividend rule (or the plain default) can
        // register its own before calling the kernel and keep it.
        services.TryAddSingleton<IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<DividendPayment>>>>(
            new DividendWindowResponseEvaluator());

        // Dividend adapters own one symbol per response because DividendPayment intentionally has
        // no symbol field. Split the public batch before adapter selection so DataPipeline can
        // aggregate each payment together with its partition's provenance. TryAdd preserves a
        // consumer-supplied partitioning policy registered before the kernel composition root.
        services.TryAddSingleton<IRequestPartitioner<DividendHistoryRequest>,
            DividendHistoryRequestPartitioner>();

        // Segment-root precedence: an explicit timeSeriesDirectory parameter wins (unchanged
        // behavior); otherwise a non-default TimeSeriesSegmentRootPath option is honored; otherwise
        // the .marketdata-timeseries default. The {record-type} token is substituted per store.
        var tsRootTemplate =
            timeSeriesDirectory is not null
                ? Path.Combine(timeSeriesDirectory, "{record-type}")
                : !string.Equals(
                    pipelineOptions.TimeSeriesSegmentRootPath,
                    new DataPipelineOptions().TimeSeriesSegmentRootPath,
                    StringComparison.Ordinal)
                    ? pipelineOptions.TimeSeriesSegmentRootPath
                    : Path.Combine(AppContext.BaseDirectory, ".marketdata-timeseries", "{record-type}");

        if (!tsRootTemplate.Contains("{record-type}", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "TimeSeriesSegmentRootPath must contain the '{record-type}' token. The kernel "
                + "registers multiple record stores; without the token they would use one shared folder "
                + "for every segment and WAL. Use timeSeriesDirectory for an automatically isolated root, or "
                + "include the token in the configured template.",
                nameof(configurePipeline));
        }

        // Register the kernel's default store set at most once per service collection.
        // A repeated AddMarketDataKernel (legitimate for test fixtures that compose multiple host
        // builders — see the pipeline-options guard above) must NOT register a SECOND set of store
        // registrations over the same segment folders: each registration opens its LSM engine/WAL
        // when the registry drains it (deferred open), so a doubled registration set opens
        // two engines over one folder — and Storage 2.0's WAL takes no exclusive lock, so the
        // double-open neither throws nor is caught, silently leaking the first engine set. The marker
        // mirrors the Configure-guard's own once-per-collection idempotency intent. Per-record
        // overrides via AddTimeSeriesStore/ConfigureTimeSeriesStore are unaffected (public path, no
        // marker) so per-record opt-out still composes on top of the defaults.
        if (!services.Any(d => d.ServiceType == typeof(DefaultTimeSeriesStoresMarker)))
        {
            services.AddSingleton(new DefaultTimeSeriesStoresMarker());

            AddDefaultColumnarStore<PriceBarDatasetKey, Bar>(services, tsRootTemplate, pipelineOptions);
            AddDefaultColumnarStore<FxRateDatasetKey, FxRate>(services, tsRootTemplate, pipelineOptions);
            AddDefaultColumnarStore<FactorObservationDatasetKey, FactorObservation>(services, tsRootTemplate, pipelineOptions);
            AddDefaultColumnarStore<ScalarObservationDatasetKey, ScalarObservation>(services, tsRootTemplate, pipelineOptions);
            AddDefaultColumnarStore<YieldCurveQuoteDatasetKey, YieldCurveQuote>(services, tsRootTemplate, pipelineOptions);

            // Row-format stores for the [StorageSerializable]-only records (enabled by
            // Storage 2.0's Option-A constraint relocation).
            // These records cannot register a columnar store (no IColumnarRehydratable), so they
            // register a row store store-only (no snapshot engine — a documented row limitation).
            AddDefaultRowStore<OptionQuoteDatasetKey, OptionQuote>(services, tsRootTemplate, pipelineOptions);
            AddDefaultRowStore<FuturesSettlementDatasetKey, FuturesSettlement>(services, tsRootTemplate, pipelineOptions);
            AddDefaultRowStore<DividendPaymentDatasetKey, DividendPayment>(services, tsRootTemplate, pipelineOptions);
        }

        // Dataset-key builders — needed by DataPipeline to project a request into its
        // dataset key. Registered here so they are DI-resolvable now (previously absent). The
        // provider string is a default; downstream adapters re-register per concrete provider.
        services.TryAddSingleton<IDatasetKeyBuilder<PriceHistoryRequest, PriceBarDatasetKey>>(
            new PriceBarDatasetKeyBuilder("default"));
        services.TryAddSingleton<IDatasetKeyBuilder<EconomicSeriesRequest, ScalarObservationDatasetKey>>(
            new EconomicSeriesDatasetKeyBuilder("default"));
        services.TryAddSingleton<IDatasetKeyBuilder<FxHistoryRequest, FxRateDatasetKey>>(
            new FxRateDatasetKeyBuilder("default"));
        services.TryAddSingleton<IDatasetKeyBuilder<FactorSeriesRequest, FactorObservationDatasetKey>>(
            new FactorSeriesDatasetKeyBuilder("default"));

        // Per-factor fan-out for FactorObservation:
        // the presence of this IDatasetKeyFanOut is what activates the fan-out append + cross-key
        // read merge in DataPipeline for FactorSeriesRequest. The provider column matches the key
        // builder above so append keys and read-scan bounds agree. The ICrossKeyScan half is already
        // registered generically for FactorObservation by AddDefaultColumnarStore above (every columnar
        // record gets one); only the fan-out registration was missing to make the capability live.
        services.TryAddSingleton<
            IDatasetKeyFanOut<FactorSeriesRequest, FactorObservationDatasetKey, FactorObservation, FactorObservation>>(
            new FactorObservationFanOut("default"));
        services.TryAddSingleton<IDatasetKeyBuilder<FuturesSettlementRequest, FuturesSettlementDatasetKey>>(
            new FuturesSettlementDatasetKeyBuilder("default"));
        services.TryAddSingleton<
            IDatasetKeyFanOut<FuturesSettlementRequest, FuturesSettlementDatasetKey, FuturesSettlement, FuturesSettlement>>(
            new FuturesSettlementFanOut("default"));
        services.TryAddSingleton<IDatasetKeyBuilder<YieldCurveQuoteRequest, YieldCurveQuoteDatasetKey>>(
            new YieldCurveQuoteDatasetKeyBuilder("default"));

        // Per-tenor fan-out for YieldCurveQuote:
        // the presence of this IDatasetKeyFanOut is what activates the fan-out append + cross-key
        // read merge in DataPipeline for YieldCurveQuoteRequest. The provider column matches the key
        // builder above so append keys and read-scan bounds agree.
        services.TryAddSingleton<
            IDatasetKeyFanOut<YieldCurveQuoteRequest, YieldCurveQuoteDatasetKey, YieldCurveQuote, YieldCurveQuote>>(
            new YieldCurveQuoteFanOut("default"));
        services.TryAddSingleton<IDatasetKeyBuilder<OptionChainRequest, OptionQuoteDatasetKey>>(
            new OptionQuoteDatasetKeyBuilder("default"));
        services.TryAddSingleton<IDatasetKeyBuilder<DividendHistoryRequest, DividendPaymentDatasetKey>>(
            new DividendPaymentDatasetKeyBuilder("default"));

        // Per-contract fan-out for OptionQuote:
        // the keyed-row envelope. This is the FETCHED-differs-from-STORED case: the option-
        // chain adapter emits OptionChainRow (contract identity + quote body) and the fan-out projects
        // each row to its own OptionQuoteDatasetKey(Provider, Underlying, Expiry, Strike, Type), storing
        // the slim OptionQuote body under it. The presence of this IDatasetKeyFanOut (and the row
        // cross-key scan registered by AddDefaultRowStore) is what activates the fan-out append +
        // cross-key read merge for OptionChainRequest. Provider column matches the key builder above.
        services.TryAddSingleton<
            IDatasetKeyFanOut<OptionChainRequest, OptionQuoteDatasetKey, OptionChainRow, OptionQuote>>(
            new OptionChainRowFanOut("default"));

        // The recovery indexes are bounded L1-aligned accelerators. Durable checkpoint metadata is
        // the restart authority for provider attribution and source-confirmed empty ranges.
        services.TryAddSingleton<IAttemptedRangeIndex>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<DataPipelineOptions>>().Value;
            return new InMemoryAttemptedRangeIndex(
                options.TimeSeriesRecoveryIndexCapacity,
                options.TimeSeriesRecoveryIndexTtl,
                serviceProvider.GetRequiredService<IClock>());
        });
        services.TryAddSingleton<ITimeSeriesSelectedProviderIndex>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<DataPipelineOptions>>().Value;
            return new InMemoryTimeSeriesSelectedProviderIndex(
                options.TimeSeriesRecoveryIndexCapacity,
                options.TimeSeriesRecoveryIndexTtl,
                serviceProvider.GetRequiredService<IClock>());
        });
        services.TryAddSingleton<ITimeSeriesRecoveryMetadataStore, TimeSeriesRecoveryMetadataStore>();

        // Catalogue of registered (TRequest, TDatasetKey) pairs so the time-series pipeline can
        // discover the TDatasetKey for a request's runtime type — the container cannot resolve an
        // open-generic IDatasetKeyBuilder<,>, so the closed interface types are recorded here.
        // Kept in lockstep with the TryAddSingleton builder registrations above.
        services.TryAddSingleton<IDatasetKeyBuilderCatalog>(new DatasetKeyBuilderCatalog(
        [
            typeof(IDatasetKeyBuilder<PriceHistoryRequest, PriceBarDatasetKey>),
            typeof(IDatasetKeyBuilder<EconomicSeriesRequest, ScalarObservationDatasetKey>),
            typeof(IDatasetKeyBuilder<FxHistoryRequest, FxRateDatasetKey>),
            typeof(IDatasetKeyBuilder<FactorSeriesRequest, FactorObservationDatasetKey>),
            typeof(IDatasetKeyBuilder<FuturesSettlementRequest, FuturesSettlementDatasetKey>),
            typeof(IDatasetKeyBuilder<YieldCurveQuoteRequest, YieldCurveQuoteDatasetKey>),
            typeof(IDatasetKeyBuilder<OptionChainRequest, OptionQuoteDatasetKey>),
            typeof(IDatasetKeyBuilder<DividendHistoryRequest, DividendPaymentDatasetKey>),
        ]));

        // Catalogue of registered fan-out (TRequest, TDatasetKey, TFetched, TStored) quadruples so the
        // time-series pipeline can DISCOVER the fetched-row type for a request — the keyed-row envelope
        // makes TFetched differ from the stored TRecord (OptionChainRow vs OptionQuote), so the pipeline
        // cannot form the closed fan-out interface from the stored type alone. The body-discriminant
        // fan-outs (YieldCurve/Factor) are the identity case (TFetched == TStored). Kept in lockstep
        // with the TryAddSingleton fan-out registrations above.
        services.TryAddSingleton<IDatasetKeyFanOutCatalog>(new DatasetKeyFanOutCatalog(
        [
            typeof(IDatasetKeyFanOut<YieldCurveQuoteRequest, YieldCurveQuoteDatasetKey, YieldCurveQuote, YieldCurveQuote>),
            typeof(IDatasetKeyFanOut<FactorSeriesRequest, FactorObservationDatasetKey, FactorObservation, FactorObservation>),
            typeof(IDatasetKeyFanOut<FuturesSettlementRequest, FuturesSettlementDatasetKey, FuturesSettlement, FuturesSettlement>),
            typeof(IDatasetKeyFanOut<OptionChainRequest, OptionQuoteDatasetKey, OptionChainRow, OptionQuote>),
        ]));

        return services;
    }

    // Audit trail (not doc-comment content — it must not ship in the packaged XML documentation,
    // where a private spec filename resolves to nothing for a consumer): this notice and its
    // deferred-resolution mechanism describe the normalizers the provider actually resolves.

    /// <summary>
    /// Whether the resolved <see cref="DataPipelineOptions"/> asks for the kernel's default
    /// normalizers. Read from the built provider by each normalizer factory, so the answer is
    /// downstream of every options channel — <c>configurePipeline</c>, a host <c>Configure</c> in
    /// either order, <c>PostConfigure</c>, <c>ConfigureOptions&lt;T&gt;()</c>, or
    /// <c>IConfiguration</c> section binding.
    /// </summary>
    /// <param name="provider">The provider the normalizer is being resolved from.</param>
    /// <returns>
    /// <see langword="true"/> when the resolved options request the kernel defaults; otherwise
    /// <see langword="false"/>, which makes the factory yield <see langword="null"/>.
    /// </returns>
    private static bool DefaultNormalizersRequested(IServiceProvider provider)
        => provider.GetService<IOptions<DataPipelineOptions>>()
            is { Value.RegisterDefaultNormalizers: true };

    /// <summary>
    /// Emits the normalizer-registration notice describing the <b>effective</b> post-composition
    /// state — whether the kernel's own default normalizers ended up registered — so that neither
    /// outcome is a silent skip a host has no way to detect. When nothing was registered but the
    /// resolved <see cref="DataPipelineOptions"/> asks for the defaults, the notice reports that
    /// disagreement rather than asserting a flag value the container contradicts; when the host
    /// owns normalization itself, it says so rather than claiming records are un-normalized.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Logger resolution — why this runs at first resolution, not at composition time.</b>
    /// <see cref="AddMarketDataKernel"/> runs before any <see cref="IServiceProvider"/> exists, so
    /// at composition time there is nothing to resolve an <see cref="ILoggerFactory"/> <i>from</i>.
    /// Reading the factory off the <see cref="IServiceCollection"/> instead only finds a host that
    /// registered a factory <b>instance</b>; the idiomatic registration —
    /// <c>services.AddLogging(...)</c> — registers it by implementation type, so a descriptor scan
    /// misses it and every real host gets silence. Building a provider mid-composition to fix that
    /// is not an option either: it would eagerly realize the kernel's singletons, including the
    /// lazily-opened time-series engines.
    /// </para>
    /// <para>
    /// This method is therefore invoked from inside the <see cref="IDataPipeline"/> singleton
    /// factory, against the host's own built provider — the same deferred pattern
    /// <c>ResilientHttpDataTransport</c>'s logger already uses. Deferred resolution is a strict
    /// superset of the descriptor scan: a built provider resolves instance-registered factories
    /// too. The emission point shifts from composition to <b>first
    /// <see cref="IDataPipeline"/> resolution</b>, which is when a host actually begins using the
    /// kernel; a host that never resolves the pipeline never runs it and so cannot suffer the
    /// un-normalized-data failure mode this notice exists to surface. Being a singleton factory, it
    /// fires exactly once per built provider regardless of how often the pipeline is resolved. The
    /// notice is diagnostic only: it never changes what is registered.
    /// </para>
    /// <para>
    /// A host whose logging is filtered below <see cref="LogLevel.Information"/> (or
    /// <see cref="LogLevel.Debug"/> for the off-branch) will not see the notice. That is the host's
    /// own configured filter, deliberately not defeated here: the off-branch is intentionally not
    /// raised to <see cref="LogLevel.Warning"/>, because <see langword="false"/> is the supported
    /// default and a warning on the default path trains hosts to ignore it.
    /// </para>
    /// </remarks>
    /// <param name="provider">
    /// The host's built provider, supplying the logger factory and — since the amended registration
    /// is a resolution-time factory — the normalizers themselves.
    /// </param>
    private static void LogNormalizerComposition(IServiceProvider provider)
    {
        var logger = provider
            .GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(ServiceCollectionExtensions).FullName!);

        if (logger is null)
        {
            return;
        }

        // Derived by RESOLVING each record type off the built provider, not by scanning the
        // collection's descriptors. The resolution-time registration is a factory, and a factory
        // descriptor carries no ImplementationInstance — the previous scan (which matched on the
        // instance's concrete type) would score zero in EVERY shape and report "not registered" for
        // a container that resolves all nine. Resolution is also strictly more truthful: it
        // observes what the pipeline itself will get, including the flag value, host overrides in
        // either order, and per-record RemoveAll opt-outs, on any call of a repeated composition.
        //
        // Kernel defaults only: a record type counts as a kernel default when what resolves is the
        // shipped implementation for that record type. That is what separates the kernel's own
        // registration from one TryAddSingleton YIELDED to, and it keeps a host's normalizer for a
        // record type the kernel ships none for (InstrumentMetadata) out of the count entirely —
        // the loop never asks about a record type outside the shipped nine.
        var kernelDefaults = new List<string>(s_kernelDefaultNormalizers.Length);
        var hostOwned = new List<string>(s_kernelDefaultNormalizers.Length);

        foreach (var (serviceType, recordType, implementationType) in s_kernelDefaultNormalizers)
        {
            var resolved = provider.GetService(serviceType);
            if (resolved is null)
            {
                continue;
            }

            if (resolved.GetType() == implementationType)
            {
                kernelDefaults.Add(recordType);
            }
            else
            {
                hostOwned.Add(recordType);
            }
        }

        kernelDefaults.Sort(StringComparer.Ordinal);
        hostOwned.Sort(StringComparer.Ordinal);

        // Branch on the composed state, not on a flag captured by one call — see the notice
        // registration in AddMarketDataKernel for why a captured flag inverts under repetition.
        if (kernelDefaults.Count > 0)
        {
            logger.LogInformation(
                "Registered {Count} default normalizers (record types: {RecordTypes}).",
                kernelDefaults.Count,
                string.Join(", ", kernelDefaults));
        }
        else if (hostOwned.Count > 0)
        {
            // The host owns normalization for every record type the kernel would have covered —
            // the host-override-wins path, i.e. a CORRECT composition. Reporting it as
            // "records pass through un-normalized" (the shape this branch replaces) was
            // affirmatively false: the host's normalizer resolves and DataPipeline uses it. Because
            // the derivation resolves rather than scans, "nothing resolves" and "the host's own
            // resolves" are now distinguishable states, so that false alarm cannot be reached.
            logger.LogDebug(
                "Kernel default normalizers not registered (RegisterDefaultNormalizers = {Flag}); "
                + "the host registers its own INormalizer<T,T> for {HostCount} of the {TotalCount} "
                + "record types the kernel ships normalizers for ({RecordTypes}). Record types the "
                + "host does not cover are not normalized.",
                DefaultNormalizersRequested(provider),
                hostOwned.Count,
                s_kernelDefaultNormalizers.Length,
                string.Join(", ", hostOwned));
        }
        else if (DefaultNormalizersRequested(provider))
        {
            // Nothing resolves for any of the nine, yet the resolved options ask for the defaults.
            // The notice must not assert "RegisterDefaultNormalizers = false" here — that states
            // the negation of the host's own configuration and steers an operator debugging
            // un-normalized data into concluding the configuration never bound.
            //
            // Under the resolution-time registration the flag is read at RESOLUTION time, so an options
            // channel or ordering can no longer produce this state: every channel reaches the
            // factory. What remains reachable is the per-record opt-out applied to every
            // record type — services.RemoveAll<INormalizer<T,T>>() nine times with the flag left
            // on — which is why the remediation names that and not a composition-time flag read.
            // The branch is kept rather than deleted because it is the guard that keeps the
            // "un-normalized" claim tied to an OBSERVED empty resolution instead of an inferred
            // one.
            //
            // Warning, not Debug: the off-branch stays at Debug because false is the supported
            // default and warning on the default path trains hosts to ignore the notice. That
            // rationale does not cover this state — the host asked for normalizers and silently did
            // not get them — so the default path keeps its Debug line below and only the
            // disagreement is raised.
            logger.LogWarning(
                "Default normalizers not registered, but the resolved DataPipelineOptions reports "
                + "RegisterDefaultNormalizers = true; no INormalizer<T,T> resolves for any of the "
                + "record types the kernel ships normalizers for, so records pass through "
                + "un-normalized. The kernel's registrations read the flag when they are resolved, "
                + "so this state means every record type was opted out after the kernel call with "
                + "services.RemoveAll<INormalizer<T,T>>(): drop the RemoveAll calls for the record "
                + "types that should be normalized, or turn RegisterDefaultNormalizers off so the "
                + "two surfaces agree.");
        }
        else
        {
            logger.LogDebug(
                "Default normalizers not registered (RegisterDefaultNormalizers = false); records "
                + "pass through un-normalized unless the host registers INormalizer<T,T> itself.");
        }
    }

    /// <summary>
    /// The nine record types the <c>RegisterDefaultNormalizers</c> block registers, each paired
    /// with its closed <c>INormalizer&lt;T,T&gt;</c> service type and the shipped implementation.
    /// The composition notice resolves each service type off the built provider and counts a record
    /// type as a kernel default only when the shipped implementation is what came back, so a host's
    /// own <c>INormalizer&lt;T,T&gt;</c> is never reported as a kernel default. Kept in lockstep
    /// with that registration block — the drift guard over the Normalization assembly's exported
    /// normalizers pins the same set.
    /// </summary>
    private static readonly (Type ServiceType, string RecordType, Type ImplementationType)[]
        s_kernelDefaultNormalizers =
    [
        (typeof(INormalizer<Bar, Bar>), nameof(Bar), typeof(BarNormalizer)),
        (typeof(INormalizer<CorporateAction, CorporateAction>), nameof(CorporateAction), typeof(CorporateActionNormalizer)),
        (typeof(INormalizer<DividendPayment, DividendPayment>), nameof(DividendPayment), typeof(DividendPaymentNormalizer)),
        (typeof(INormalizer<FactorObservation, FactorObservation>), nameof(FactorObservation), typeof(FactorObservationNormalizer)),
        (typeof(INormalizer<FuturesSettlement, FuturesSettlement>), nameof(FuturesSettlement), typeof(FuturesSettlementNormalizer)),
        (typeof(INormalizer<FxRate, FxRate>), nameof(FxRate), typeof(FxRateNormalizer)),
        (typeof(INormalizer<OptionQuote, OptionQuote>), nameof(OptionQuote), typeof(OptionQuoteNormalizer)),
        (typeof(INormalizer<ScalarObservation, ScalarObservation>), nameof(ScalarObservation), typeof(ScalarObservationNormalizer)),
        (typeof(INormalizer<YieldCurveQuote, YieldCurveQuote>), nameof(YieldCurveQuote), typeof(YieldCurveQuoteNormalizer)),
    ];

    /// <summary>
    /// Registers one columnar time-series store for a record type into the kernel's registry,
    /// shaped by the <see cref="DataPipelineOptions"/> store-shaping properties.
    /// With default options this reproduces the shipped defaults — SizeTiered
    /// compaction, group-commit WAL on, bloom filter on, WiscKey off, columnar format,
    /// <see cref="QuarantineBehavior.TreatAsGap"/> quarantine.
    /// </summary>
    private static void AddDefaultColumnarStore<TDatasetKey, TRecord>(
        IServiceCollection services,
        string segmentRootTemplate,
        DataPipelineOptions options)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
        => services.AddTimeSeriesStore<TDatasetKey, TRecord>(
            b => ApplyStoreShapingOptions(b, segmentRootTemplate, options));

    /// <summary>
    /// Registers one <b>row-format</b> time-series store for a <c>[StorageSerializable]</c>-only
    /// record type, shaped by the <see cref="DataPipelineOptions"/>
    /// store-shaping properties applicable to a row store (root, compaction, WAL, value-log). With
    /// default options this reproduces the shipped defaults — SizeTiered compaction, group-commit
    /// WAL on, value-log off, store-only registration (no snapshot engine).
    /// </summary>
    private static void AddDefaultRowStore<TDatasetKey, TRecord>(
        IServiceCollection services,
        string segmentRootTemplate,
        DataPipelineOptions options)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new()
        => services.AddRowTimeSeriesStore<TDatasetKey, TRecord>(
            o => ApplyRowStoreShapingOptions(o, segmentRootTemplate, options));

    private static void ValidateStoreShapingOptions(DataPipelineOptions options)
    {
        if (!Enum.IsDefined(options.TimeSeriesSegmentFormat))
        {
            throw new ArgumentOutOfRangeException(nameof(options),
                options.TimeSeriesSegmentFormat, "The time-series segment format must be Row or Columnar.");
        }

        ArgumentNullException.ThrowIfNull(options.TimeSeriesBloomFilter);
        _ = new Boutquin.Storage.Domain.ValueObjects.SortedStringTable.SegmentBloomFilterOptions<TimeSeriesKey<PriceBarDatasetKey>>(
            options.TimeSeriesBloomFilter.Enabled,
            options.TimeSeriesBloomFilter.ExpectedItems,
            options.TimeSeriesBloomFilter.FalsePositiveRate);
    }

    /// <summary>
    /// Threads the <see cref="DataPipelineOptions"/> store-shaping properties into a columnar
    /// <see cref="TimeSeriesStoreBuilder{TDatasetKey, TRecord}"/>. Default
    /// option values reproduce the builder's own field defaults exactly, so a caller who sets
    /// nothing gets today's behavior. Value-log dispatch is threaded only when the format is row and
    /// a finite threshold is set (columnar rejects non-inline entries at the Storage layer).
    /// </summary>
    internal static void ApplyStoreShapingOptions<TDatasetKey, TRecord>(
        TimeSeriesStoreBuilder<TDatasetKey, TRecord> builder,
        string segmentRootTemplate,
        DataPipelineOptions options)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        ValidateStoreShapingOptions(options);

        if (options.TimeSeriesSegmentFormat == TimeSeriesSegmentFormat.Row)
        {
            builder.UseRowFormat();
        }
        else
        {
            builder.UseColumnarFormat();
        }

        builder
            .WithSegmentRoot(segmentRootTemplate)
            .WithCompactionStrategy(options.TimeSeriesCompactionStrategy)
            .WithQuarantineBehavior(options.QuarantineBehavior)
            .WithWriteAheadLogOptions(w =>
            {
                w.GroupCommitBatchSize = options.TimeSeriesWriteAheadLog.GroupCommitBatchSize;
                w.GroupCommitLatencyMs = options.TimeSeriesWriteAheadLog.GroupCommitLatencyMs;
                w.SegmentMaxBytes = options.TimeSeriesWriteAheadLog.SegmentMaxBytes;
            })
            .WithBloomFilter(options.TimeSeriesBloomFilter);

        if (options.DataQualityIncidentSink is { } incidentSink)
        {
            builder.WithDataQualityIncidentSink(incidentSink);
        }

        // WiscKey value-log dispatch is valid only on the row format.
        if (builder.SegmentFormat == TimeSeriesSegmentFormat.Row
            && options.TimeSeriesValueLog is { } valueLog)
        {
            builder.UseValueLog(v => v.ValueLogThresholdBytes = valueLog.ValueLogThresholdBytes);
        }
    }

    /// <summary>
    /// Threads the <see cref="DataPipelineOptions"/> store-shaping properties applicable to a
    /// row store into <see cref="RowTimeSeriesStoreOptions"/>. Segment format
    /// is fixed for a row-only record; Bloom settings configure its physical SST filters.
    /// </summary>
    internal static void ApplyRowStoreShapingOptions(
        RowTimeSeriesStoreOptions rowOptions,
        string segmentRootTemplate,
        DataPipelineOptions options)
    {
        ArgumentNullException.ThrowIfNull(rowOptions);
        ArgumentNullException.ThrowIfNull(options);

        ValidateStoreShapingOptions(options);

        rowOptions.SegmentRoot = segmentRootTemplate;
        rowOptions.CompactionStrategy = options.TimeSeriesCompactionStrategy;
        rowOptions.WriteAheadLog = options.TimeSeriesWriteAheadLog;
        rowOptions.BloomFilter = options.TimeSeriesBloomFilter;

        rowOptions.ValueLog = options.TimeSeriesValueLog;
    }

    /// <summary>
    /// Builds a columnar (or row-format) time-series store for the given <c>(TDatasetKey, TRecord)</c>
    /// pair via <see cref="TimeSeriesStoreBuilder{TDatasetKey, TRecord}"/> and registers it into the
    /// <see cref="IMutableTimeSeriesStoreRegistry"/> so <c>DataPipeline</c> resolves a live store
    /// (never an empty registry that throws on miss). The store's LSM engine/WAL is opened lazily —
    /// when the registry is first resolved and drains this registration — so a registration whose
    /// store is never used (e.g. the shipped <c>UseTimeSeriesStore=false</c> default) opens no engine
    /// and leaks no handle, and the container disposes every engine it does open.
    /// </summary>
    /// <typeparam name="TDatasetKey">Dataset partition key type.</typeparam>
    /// <typeparam name="TRecord">
    /// Dated, <c>[ColumnarStorable]</c> record type. The columnar source generator emits the
    /// <see cref="IColumnarRehydratable{TSelf}"/> the Storage LSM time-series stack requires.
    /// </typeparam>
    /// <param name="services">The service collection. Must already carry an
    /// <see cref="IMutableTimeSeriesStoreRegistry"/> (registered by <see cref="AddMarketDataKernel"/>).</param>
    /// <param name="configure">Optional per-store override of the defaults.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddTimeSeriesStore<TDatasetKey, TRecord>(
        this IServiceCollection services,
        Action<TimeSeriesStoreBuilder<TDatasetKey, TRecord>>? configure = null)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, IColumnarRehydratable<TRecord>, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        var builder = new TimeSeriesStoreBuilder<TDatasetKey, TRecord>(typeof(TRecord).Name);
        configure?.Invoke(builder);

        // Ensure the draining registry is registered so a standalone AddTimeSeriesStore call
        // (outside AddMarketDataKernel) still produces a resolvable registry.
        AddTimeSeriesStoreRegistry(services);

        // Register the store + engine as a pending registration whose FACTORY opens the LSM
        // engine/WAL LAZILY, on first realization. The registry singleton's factory drains every
        // ITimeSeriesStoreRegistration into the registry when the registry is first resolved, so a
        // store added before the registry exists is still applied; ApplyTo registers both the store
        // (Register) and the engine (RegisterEngine). Surfacing the engine alongside the store backs
        // the snapshot-isolated read path (ResolveEngine) with the SAME instance the store
        // reads through, so a pinned snapshot stays coherent with the store's data.
        //
        // WHY the FACTORY builds the stack (container-owned disposal
        // requires realization): MS DI owns disposal of a factory-created singleton, but ONLY once it has
        // realized it (the factory runs on first resolve, and that resolve is what enters the
        // instance into the provider's disposal-tracking list). ITimeSeriesStoreRegistration is
        // realized only when the registry drains it — i.e. only when the registry is resolved
        // (UseTimeSeriesStore=true / a snapshot acquisition). Building the engine EAGERLY here while
        // realization stays lazy was the leak: in the shipped UseTimeSeriesStore=false default the
        // registry is never resolved, so the eagerly-opened engines/WALs were never disposal-tracked
        // and provider.DisposeAsync() released none of them. Deferring the open into the factory
        // makes open and disposal-tracking happen on the SAME realization: in the default config
        // nothing resolves the registration, so no engine is opened and nothing leaks; once the
        // registry IS resolved, the engine is opened AND the container disposes it on provider
        // disposal. The synchronous GetAwaiter().GetResult() matches the kernel's other eager
        // singleton factories (event log, snapshot store), which also block once on first resolve.
        AddTimeSeriesStoreRegistrationSlot<TDatasetKey, TRecord>(services, sp =>
        {
            var incidentSink = sp.GetService<IDataQualityIncidentSink>()
                ?? builder.DataQualityIncidentSink;
            if (incidentSink is null && sp.GetService<ILoggerFactory>() is { } loggerFactory)
            {
                incidentSink = new LoggingDataQualityIncidentSink(
                    loggerFactory.CreateLogger<TimeSeriesStoreBuilder<TDatasetKey, TRecord>>());
            }

            if (incidentSink is not null)
            {
                builder.WithDataQualityIncidentSink(incidentSink);
            }

            var stack = builder.BuildStackAsync().GetAwaiter().GetResult();
            var store = builder.WrapStoreForQuarantineTracking(stack.Store);
            return new TimeSeriesStoreRegistration<TDatasetKey, TRecord>(store, stack.Engine);
        });

        // (row-store registration branch continues below via AddRowTimeSeriesStore.)
        // Register the PRODUCTION snapshot read binding for this record type. This is what
        // makes the snapshot-isolated read path LIVE through the kernel: DataSnapshot.ExecuteAsync
        // resolves ISnapshotReadBinding<TRecord> from this same IServiceProvider on first read, so a
        // pipeline-acquired snapshot reads through LsmSnapshotReadBinding (a real src/ type) rather
        // than throwing "No snapshot read binding is registered". The binding is format-agnostic —
        // registered for every store, same as the store/engine registration; a row-format store
        // serves snapshot reads, while the columnar default surfaces the upstream
        // columnar-rejects-snapshot limitation from AcquireSnapshotAsync (NOT a missing binding).
        // TryAdd so a pre-registered custom binding (e.g. a test double) wins.
        services.TryAddSingleton<ISnapshotReadBinding<TRecord>>(
            sp => new LsmSnapshotReadBinding<TDatasetKey, TRecord>(sp));

        // Register the cross-dataset range scan for this record type (per-row-discriminant fan-out
        // read merge). Registered for every
        // columnar record — it carries the IColumnarRehydratable constraint ResolveEngine needs, which
        // is satisfiable here — but only consulted by DataPipeline when a matching IDatasetKeyFanOut is
        // ALSO registered (fan-out record types only), so single-row types register it harmlessly and
        // never use it. TryAdd so a pre-registered custom scan (e.g. a test double) wins.
        services.TryAddSingleton<ICrossKeyScan<TDatasetKey, TRecord>>(
            sp => new LsmCrossKeyScan<TDatasetKey, TRecord>(
                sp.GetRequiredService<ITimeSeriesStoreRegistry>()));

        return services;
    }

    /// <summary>
    /// Registers a live <b>row-format</b> time-series store for a <c>[StorageSerializable]</c>-only
    /// record. Row records — <c>OptionQuote</c>, <c>FuturesSettlement</c>,
    /// <c>DividendPayment</c> — do NOT implement <c>IColumnarRehydratable</c>, so they cannot use the
    /// columnar <see cref="AddTimeSeriesStore{TDatasetKey, TRecord}"/> path; the Storage 2.0 Option-A
    /// constraint relocation lets them register through the format-agnostic store surface here. The
    /// store is registered <b>store-only</b> (no snapshot engine — see
    /// <see cref="RowTimeSeriesStoreRegistration{TDatasetKey, TRecord}"/>), so it serves the
    /// <c>DataPipeline</c> read/gap-fill path but not the snapshot-isolated read surface.
    /// </summary>
    /// <typeparam name="TDatasetKey">Dataset partition key type.</typeparam>
    /// <typeparam name="TRecord">Dated, <c>[StorageSerializable]</c>-only record type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional per-store override of the row defaults.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddRowTimeSeriesStore<TDatasetKey, TRecord>(
        this IServiceCollection services,
        Action<RowTimeSeriesStoreOptions>? configure = null)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RowTimeSeriesStoreOptions();
        configure?.Invoke(options);

        AddTimeSeriesStoreRegistry(services);

        // Factory registration whose factory opens the row engine LAZILY, on first realization, so
        // the container owns disposal AND the open is not inert in the shipped default
        // — same rationale as the columnar path in AddTimeSeriesStore: eager-open with
        // realize-on-resolve disposal leaked the engine whenever the registry was never resolved
        // (the UseTimeSeriesStore=false default). The engine is captured for disposal only (row
        // records cannot be registered into the engine/snapshot surface).
        AddTimeSeriesStoreRegistrationSlot<TDatasetKey, TRecord>(services, _ =>
        {
            var stack = RowTimeSeriesStoreFactory
                .BuildStackAsync<TDatasetKey, TRecord>(typeof(TRecord).Name, options)
                .GetAwaiter().GetResult();
            return new RowTimeSeriesStoreRegistration<TDatasetKey, TRecord>(stack.Store, stack.Engine);
        });

        // Register the ROW-format cross-dataset range scan for this record type (per-row-discriminant
        // fan-out read merge). A
        // row-format record (OptionQuote, the B′ envelope's stored type) has no columnar engine, so the
        // columnar LsmCrossKeyScan (which resolves ResolveEngine, IColumnarRehydratable-constrained)
        // cannot serve it; RowLsmCrossKeyScan resolves the row engine via the row-safe ResolveScanEngine
        // instead. Registered for every row store but only consulted by DataPipeline when a matching
        // IDatasetKeyFanOut is ALSO registered (fan-out record types only), so non-fan-out row records
        // (DividendPayment) register it harmlessly and never use it. TryAdd so a
        // pre-registered custom scan (e.g. a test double) wins.
        services.TryAddSingleton<ICrossKeyScan<TDatasetKey, TRecord>>(
            sp => new RowLsmCrossKeyScan<TDatasetKey, TRecord>(
                sp.GetRequiredService<ITimeSeriesStoreRegistry>()));

        return services;
    }

    /// <summary>
    /// Per-record-type store opt-out convenience. The library default wires a
    /// columnar store for every <c>[ColumnarStorable]</c> record; this method lets a caller register
    /// a specific <c>(TDatasetKey, TRecord)</c> pair as a <b>row</b> store instead — the
    /// <c>ConfigureTimeSeriesStore&lt;…&gt;(b =&gt; b.UseRowFormat())</c> pattern exposed on
    /// the <c>DataPipelineOptions.TimeSeriesSegmentFormat</c> XML remark. It composes over
    /// <see cref="AddRowTimeSeriesStore{TDatasetKey, TRecord}"/>; the row-safe constraint set means it
    /// serves both <c>[ColumnarStorable]</c> records (opting them out of columnar) and
    /// <c>[StorageSerializable]</c>-only records.
    /// </summary>
    /// <typeparam name="TDatasetKey">Dataset partition key type.</typeparam>
    /// <typeparam name="TRecord">Dated record type to register as a row store.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">
    /// Configures the row store via a <see cref="RowStoreConfigurator"/>. Call
    /// <see cref="RowStoreConfigurator.UseRowFormat"/> (the row format is already implied) and set
    /// segment-root / compaction / WAL / value-log overrides.
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection ConfigureTimeSeriesStore<TDatasetKey, TRecord>(
        this IServiceCollection services,
        Action<RowStoreConfigurator> configure)
        where TDatasetKey : ISerializable<TDatasetKey>, IComparable<TDatasetKey>, new()
        where TRecord : ISerializable<TRecord>, IHasDate, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var configurator = new RowStoreConfigurator();
        configure(configurator);

        RemoveTimeSeriesStoreRegistrationSlot<TDatasetKey, TRecord>(services);
        return services.AddRowTimeSeriesStore<TDatasetKey, TRecord>(configurator.Apply);
    }

    private static void AddTimeSeriesStoreRegistrationSlot<TDatasetKey, TRecord>(
        IServiceCollection services,
        Func<IServiceProvider, ITimeSeriesStoreRegistration> factory)
    {
        var registration = ServiceDescriptor.Singleton(typeof(ITimeSeriesStoreRegistration), factory);
        services.Add(registration);
        services.AddSingleton(new TimeSeriesStoreRegistrationSlot<TDatasetKey, TRecord>(registration));
    }

    private static void RemoveTimeSeriesStoreRegistrationSlot<TDatasetKey, TRecord>(IServiceCollection services)
    {
        var markerDescriptors = services
            .Where(descriptor =>
                descriptor.ServiceType == typeof(TimeSeriesStoreRegistrationSlot<TDatasetKey, TRecord>)
                && descriptor.ImplementationInstance is TimeSeriesStoreRegistrationSlot<TDatasetKey, TRecord>)
            .ToList();

        foreach (var markerDescriptor in markerDescriptors)
        {
            var marker = (TimeSeriesStoreRegistrationSlot<TDatasetKey, TRecord>)markerDescriptor.ImplementationInstance!;
            services.Remove(marker.RegistrationDescriptor);
            services.Remove(markerDescriptor);
        }
    }

    /// <summary>
    /// Registers the <see cref="IMutableTimeSeriesStoreRegistry"/> / <see cref="ITimeSeriesStoreRegistry"/>
    /// singletons whose factory drains every <see cref="ITimeSeriesStoreRegistration"/> added via
    /// <see cref="AddTimeSeriesStore{TDatasetKey, TRecord}"/> into a live registry on first resolve.
    /// Idempotent — safe to call from both <see cref="AddMarketDataKernel"/> and a standalone
    /// <see cref="AddTimeSeriesStore{TDatasetKey, TRecord}"/>.
    /// </summary>
    private static void AddTimeSeriesStoreRegistry(IServiceCollection services)
    {
        services.TryAddSingleton<IMutableTimeSeriesStoreRegistry>(sp =>
        {
            var registry = new DefaultTimeSeriesStoreRegistry();
            foreach (var registration in sp.GetServices<ITimeSeriesStoreRegistration>())
            {
                registration.ApplyTo(registry);
            }

            return registry;
        });
        services.TryAddSingleton<ITimeSeriesStoreRegistry>(
            sp => sp.GetRequiredService<IMutableTimeSeriesStoreRegistry>());
    }

    /// <summary>
    /// Registers <see cref="WriteThroughCache{TKey, TData}"/> for an additional record type not
    /// covered by the kernel defaults. Use when a downstream repo ships its own normalizer and
    /// needs L2 caching for the normalized record.
    /// </summary>
    /// <typeparam name="TRecord">The record type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddMarketDataWriteThroughFor<TRecord>(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddWriteThroughFor<TRecord>(services);
        return services;
    }

    /// <summary>
    /// Registers <see cref="WriteThroughCache{TKey, TData}"/> for
    /// <c>SnapshotEnvelope&lt;IReadOnlyList&lt;TRecord&gt;&gt;</c> so the pipeline
    /// can persist L2 snapshots for the given record type.
    /// </summary>
    private static void AddWriteThroughFor<TRecord>(IServiceCollection services)
    {
        // Inner payload serializer (JSON for the record list)
        services.TryAddSingleton<ISnapshotSerializer<IReadOnlyList<TRecord>>>(
            _ => new JsonSnapshotSerializer<IReadOnlyList<TRecord>>());

        // Envelope serializer (wraps inner with metadata header)
        services.TryAddSingleton<ISnapshotSerializer<SnapshotEnvelope<IReadOnlyList<TRecord>>>>(sp =>
            new SnapshotEnvelopeSerializer<IReadOnlyList<TRecord>>(
                sp.GetRequiredService<ISnapshotSerializer<IReadOnlyList<TRecord>>>()));

        // Write-through cache wired to the snapshot store
        services.TryAddSingleton(sp =>
        {
            var store = sp.GetRequiredService<ISnapshotStore>();
            var serializer = sp.GetRequiredService<ISnapshotSerializer<SnapshotEnvelope<IReadOnlyList<TRecord>>>>();
            return new WriteThroughCache<string, SnapshotEnvelope<IReadOnlyList<TRecord>>>(
                store, serializer, key => key);
        });
    }

    /// <summary>
    /// Presence-only marker recording that <see cref="AddMarketDataKernel"/> already registered its
    /// default time-series store set on a given service collection, so a repeated
    /// <see cref="AddMarketDataKernel"/> does not double-open the LSM engines/WALs.
    /// Never resolved; carries no state or resources.
    /// </summary>
    private sealed class DefaultTimeSeriesStoresMarker;

    private sealed record TimeSeriesStoreRegistrationSlot<TDatasetKey, TRecord>(
        ServiceDescriptor RegistrationDescriptor);
}
