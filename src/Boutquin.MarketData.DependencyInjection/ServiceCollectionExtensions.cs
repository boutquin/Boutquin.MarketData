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
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Cache.Memory;
using Boutquin.MarketData.Cache.Policies;
using Boutquin.MarketData.Cache.WriteThrough;
using Boutquin.MarketData.Calendars;
using Boutquin.MarketData.Orchestration.Coverage;
using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.MarketData.Storage.Checkpointing;
using Boutquin.MarketData.Storage.Events;
using Boutquin.MarketData.Storage.RawDocuments;
using Boutquin.MarketData.Storage.Serializers;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.MarketData.Transport.Http;
using Boutquin.Storage.Domain.Interfaces.Events;
using Boutquin.Storage.Domain.Interfaces.Schemas;
using Boutquin.Storage.Domain.Interfaces.Streaming;
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
    /// <param name="configureTransport">
    /// Optional callback to configure <see cref="TransportOptions"/> (retry, rate limiting, timeouts).
    /// </param>
    /// <param name="configurePipeline">
    /// Optional callback to configure <see cref="DataPipelineOptions"/> (L1 TTL, failure behavior).
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddMarketDataKernel(
        this IServiceCollection services,
        string? snapshotDirectory = null,
        string? rawDocumentDirectory = null,
        string? checkpointDirectory = null,
        string? eventLogDirectory = null,
        Action<TransportOptions>? configureTransport = null,
        Action<DataPipelineOptions>? configurePipeline = null)
    {
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

        // Event log. AppendOnlyEventLog creates the parent directory of the file path in its
        // constructor, so DI does not (and must not) touch System.IO.Directory directly — that
        // would leak file-system access outside the Storage package.
        var logDir = eventLogDirectory
            ?? Path.Combine(AppContext.BaseDirectory, ".marketdata-events");
        services.TryAddSingleton<IEventLog<IngestionEvent>>(
            _ => new AppendOnlyEventLog<IngestionEvent>(Path.Combine(logDir, "ingestion.log")));
        services.TryAddSingleton<IngestionEventLog>();

        // Schema infrastructure
        services.TryAddSingleton<ISchemaCompatibilityChecker, FieldLevelCompatibilityChecker>();
        services.TryAddSingleton<ISchemaRegistry>(sp =>
            new InMemorySchemaRegistry(sp.GetRequiredService<ISchemaCompatibilityChecker>()));

        // Pipeline options. Register only once per service collection so repeat calls to
        // AddMarketDataKernel (legitimate in test fixtures that compose multiple host builders)
        // don't accumulate Configure delegates whose composition order is implementation-defined.
        // Matches the TryAdd* idempotency documented for the rest of the kernel registrations.
        if (!services.Any(d =>
            d.ServiceType == typeof(IConfigureOptions<DataPipelineOptions>)))
        {
            services.Configure(configurePipeline ?? (_ => { }));
        }

        // Pipeline orchestration
        services.TryAddSingleton<IDataPipeline, DataPipeline>();
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

        return services;
    }

    /// <summary>
    /// Registers <see cref="WriteThroughCache{TKey, TData}"/> for an additional record type not
    /// covered by the kernel defaults. Use when a downstream repo ships its own normalizer and
    /// needs L2 caching for the normalized record.
    /// </summary>
    /// <typeparam name="TRecord">The record type to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMarketDataWriteThroughFor<TRecord>(this IServiceCollection services)
    {
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
}
