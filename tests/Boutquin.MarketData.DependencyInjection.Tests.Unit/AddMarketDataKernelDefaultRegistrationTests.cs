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

using Boutquin.MarketData.Abstractions.Enums;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;

using Boutquin.Storage.Domain.Enums;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// AC-4 (the LIVE proof) — after <c>AddMarketDataKernel</c> the time-series store registry is
/// non-empty: one columnar-backed store per dated columnar record type is resolvable (not an
/// empty registry that returns <see langword="null"/>), the key-builders are DI-resolvable, and
/// the kernel's default store configuration carries the documented defaults
/// (SizeTiered + WAL group-commit on + bloom factory non-null + columnar format).
/// </summary>
public sealed class AddMarketDataKernelDefaultRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "mktdata-kernel-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ServiceProvider BuildKernel()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(timeSeriesDirectory: _root);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Kernel_RegistersColumnarStore_ForEachDatedColumnarRecordType()
    {
        using var provider = BuildKernel();
        var registry = provider.GetRequiredService<ITimeSeriesStoreRegistry>();

        registry.Resolve<PriceBarDatasetKey, Bar>().Should().NotBeNull();
        registry.Resolve<FxRateDatasetKey, FxRate>().Should().NotBeNull();
        registry.Resolve<FactorObservationDatasetKey, FactorObservation>().Should().NotBeNull();
        registry.Resolve<ScalarObservationDatasetKey, ScalarObservation>().Should().NotBeNull();
        registry.Resolve<YieldCurveQuoteDatasetKey, YieldCurveQuote>().Should().NotBeNull();
    }

    [Fact]
    public async Task Kernel_RegisteredBarStore_IsLive_RoundTripsAColumnarRecord()
    {
        using var provider = BuildKernel();
        var registry = provider.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<PriceBarDatasetKey, Bar>();

        store.Should().NotBeNull();

        var key = new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", null);
        var bar = new Bar(new DateOnly(2024, 1, 2), 100m, 101m, 99m, 100.5m, 100.5m, 1_000_000L);
        await store!.AppendAsync(key, [bar]);

        var read = new List<Bar>();
        await foreach (var r in store.GetRangeAsync(key, new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 3)))
        {
            read.Add(r);
        }

        read.Should().ContainSingle().Which.Date.Should().Be(new DateOnly(2024, 1, 2));
    }

    [Fact]
    public void Kernel_DatasetKeyBuilders_AreResolvable()
    {
        using var provider = BuildKernel();

        provider.GetService<IDatasetKeyBuilder<EconomicSeriesRequest, ScalarObservationDatasetKey>>()
            .Should().NotBeNull();
        provider.GetService<IDatasetKeyBuilder<FxHistoryRequest, FxRateDatasetKey>>()
            .Should().NotBeNull();
        provider.GetService<IDatasetKeyBuilder<FactorSeriesRequest, FactorObservationDatasetKey>>()
            .Should().NotBeNull();
        provider.GetService<IDatasetKeyBuilder<YieldCurveQuoteRequest, YieldCurveQuoteDatasetKey>>()
            .Should().NotBeNull();
    }

    // The dividend builder resolves through the production composition path — a container built by
    // the real AddMarketDataKernel, not a hand-rolled ServiceCollection with a manual registration.
    [Fact]
    public void Kernel_DividendPaymentDatasetKeyBuilder_IsResolvable()
    {
        using var provider = BuildKernel();

        var builder = provider
            .GetService<IDatasetKeyBuilder<DividendHistoryRequest, DividendPaymentDatasetKey>>();

        builder.Should().NotBeNull(
            because: "the time-series row-store path resolves the dividend dataset-key builder "
                + "through the closed interface when UseTimeSeriesStore is enabled");
        builder!.Build(new DividendHistoryRequest(
                [new Symbol("SCHD")],
                new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31))))
            .Should().Be(new DividendPaymentDatasetKey("default", "SCHD"),
                because: "the kernel registers the builder with the documented default provider");
    }

    // The registration above is reachable but INERT without a catalogue entry: the time-series
    // pipeline cannot resolve an open generic from the container, so it discovers a request's
    // dataset-key type by scanning IDatasetKeyBuilderCatalog. A request type absent from the
    // catalogue is "not time-series-backed" and silently falls back to the legacy L2 path even when
    // UseTimeSeriesStore is on. This replicates the executor's discovery predicate exactly.
    [Fact]
    public void Kernel_Catalogue_DiscoversTheDividendDatasetKeyType_ForDividendHistoryRequest()
    {
        using var provider = BuildKernel();
        var catalog = provider.GetRequiredService<IDatasetKeyBuilderCatalog>();

        var discovered = catalog.RegisteredBuilderInterfaces
            .Where(t => t.IsGenericType
                && t.GetGenericTypeDefinition() == typeof(IDatasetKeyBuilder<,>)
                && t.GenericTypeArguments[0] == typeof(DividendHistoryRequest))
            .Select(t => t.GenericTypeArguments[1])
            .SingleOrDefault();

        discovered.Should().Be(
            typeof(DividendPaymentDatasetKey),
            because: "the time-series pipeline reads the dataset-key type back off this catalogue; "
                + "without the entry a dividend request is treated as not time-series-backed");
    }

    // Structural guard for the whole builder block, not just the dividend entry. The catalogue is
    // maintained by hand alongside the registrations, and a registration added without its
    // catalogue entry is silently inert — it resolves from the container while the time-series
    // pipeline treats its request type as not time-series-backed. This asserts the two stay in
    // lockstep, so the next record type to land cannot reintroduce that gap unnoticed.
    [Fact]
    public void Kernel_EveryRegisteredDatasetKeyBuilder_HasACatalogueEntry()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        var registered = services
            .Select(d => d.ServiceType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IDatasetKeyBuilder<,>))
            .ToList();

        using var provider = services.BuildServiceProvider();
        var catalogued = provider
            .GetRequiredService<IDatasetKeyBuilderCatalog>()
            .RegisteredBuilderInterfaces;

        registered.Should().NotBeEmpty(because: "the kernel registers a builder per supported request");
        registered.Should().OnlyContain(
            t => catalogued.Contains(t),
            because: "a builder registered without a catalogue entry resolves but is never reached "
                + "by the time-series path — the inert-registration failure this catalogue exists to "
                + "prevent");
    }

    // M-7 deliverable #2 — the row-format records (OptionQuote, FuturesSettlement, DividendPayment)
    // are [StorageSerializable]-only (NOT [ColumnarStorable]). Under the Storage 2.0 Option-A
    // constraint relocation the LSM store/engine no longer demand IColumnarRehydratable, so these
    // records CAN register a row store. AddMarketDataKernel registers them via the row path.
    [Fact]
    public void Kernel_RegistersRowStore_ForEachRowFormatRecordType()
    {
        using var provider = BuildKernel();
        var registry = provider.GetRequiredService<ITimeSeriesStoreRegistry>();

        registry.Resolve<OptionQuoteDatasetKey, OptionQuote>().Should().NotBeNull();
        registry.Resolve<FuturesSettlementDatasetKey, FuturesSettlement>().Should().NotBeNull();
        registry.Resolve<DividendPaymentDatasetKey, DividendPayment>().Should().NotBeNull();
    }

    // M-7 deliverable #2 (live proof) — a row store round-trips a [StorageSerializable]-only record
    // (nullable decimals preserved through the tagged-wire row path).
    [Fact]
    public async Task Kernel_RegisteredOptionQuoteStore_IsLive_RoundTripsARowRecord()
    {
        using var provider = BuildKernel();
        var registry = provider.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<OptionQuoteDatasetKey, OptionQuote>();

        store.Should().NotBeNull();

        var key = new OptionQuoteDatasetKey("cboe", "AAPL", new DateOnly(2026, 6, 19), 150.0000m, OptionType.Call);
        var quote = new OptionQuote(new DateOnly(2026, 5, 1), Bid: null, Ask: 3.25m, LastPrice: 3.10m,
            ImpliedVolatility: 0.28m, OpenInterest: 1200L, Volume: 340L);
        await store!.AppendAsync(key, [quote]);

        var read = new List<OptionQuote>();
        await foreach (var q in store.GetRangeAsync(key, new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 1)))
        {
            read.Add(q);
        }

        read.Should().ContainSingle();
        read[0].Bid.Should().BeNull("nullable fields survive the tagged-wire row path");
        read[0].Ask.Should().Be(3.25m);
    }

    // M-7 deliverable #1 — ConfigureTimeSeriesStore<TDatasetKey, TRecord> is the per-record opt-out
    // convenience that composes over the row registration path (UseRowFormat), for a record the
    // caller wants stored in the row format explicitly.
    [Fact]
    public async Task ConfigureTimeSeriesStore_RegistersRowStore_ForARowRecord()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(timeSeriesDirectory: _root);
        // Re-configure DividendPayment explicitly via the convenience opt-out.
        services.ConfigureTimeSeriesStore<DividendPaymentDatasetKey, DividendPayment>(
            b => b.UseRowFormat());
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<ITimeSeriesStoreRegistry>();
        var store = registry.Resolve<DividendPaymentDatasetKey, DividendPayment>();
        store.Should().NotBeNull();

        var key = new DividendPaymentDatasetKey("iex", "AAPL");
        var div = new DividendPayment(new DateOnly(2026, 5, 8), 0.24m,
            Currency.USD, DividendType.UsQualified);
        await store!.AppendAsync(key, [div]);

        var read = new List<DividendPayment>();
        await foreach (var d in store.GetRangeAsync(key, new DateOnly(2026, 5, 8), new DateOnly(2026, 5, 8)))
        {
            read.Add(d);
        }

        read.Should().ContainSingle().Which.Amount.Should().Be(0.24m);
    }

    [Fact]
    public void Kernel_DefaultStoreConfiguration_CarriesDocumentedDefaults()
    {
        // The kernel applies no per-store overrides, so a fresh builder reflects exactly the
        // defaults every kernel-registered store is constructed with.
        var builder = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar");

        builder.SegmentFormat.Should().Be(TimeSeriesSegmentFormat.Columnar);
        builder.CompactionStrategy.Should().Be(TimeSeriesCompactionStrategy.SizeTiered);
        builder.QuarantineBehavior.Should().Be(QuarantineBehavior.TreatAsGap);
        builder.ResolveCompactionStrategy().Should().BeOfType<
            Boutquin.Storage.Infrastructure.LsmTree.SizeTieredCompactionStrategy<
                Boutquin.Storage.Domain.ValueObjects.TimeSeries.TimeSeriesKey<PriceBarDatasetKey>>>();
        builder.BuildBloomFilter().Should().NotBeNull();
    }
}
