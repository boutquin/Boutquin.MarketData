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

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;

using Boutquin.Storage.Domain.ValueObjects.TimeSeries;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// Lifecycle guarantees for the DI-constructed time-series stores:
/// idempotent registration on a repeated <c>AddMarketDataKernel</c> (F-L1L6-02) and
/// container-owned disposal of the underlying LSM engines/WALs (F-L5-01).
/// </summary>
public sealed class TimeSeriesStoreLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "mktdata-lifecycle-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // F-L1L6-02 — a repeated AddMarketDataKernel on one IServiceCollection (the multi-host-builder
    // fixture case the file's own Configure-guard comment declares legitimate) must NOT double-open
    // a second set of LSM engines/WALs over the same segment folders. Exactly one store set per
    // record type: 5 columnar + 3 row = 8 ITimeSeriesStoreRegistration descriptors, never 16.
    [Fact]
    public void AddMarketDataKernel_CalledTwice_RegistersExactlyOneStoreSetPerRecordType()
    {
        var services = new ServiceCollection();

        services.AddMarketDataKernel(timeSeriesDirectory: _root);
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        var registrations = services.Count(
            d => d.ServiceType == typeof(ITimeSeriesStoreRegistration));

        registrations.Should().Be(
            8,
            "a repeated AddMarketDataKernel must be idempotent — 5 columnar + 3 row stores, "
            + "not a second set double-opened over the same folders");
    }

    [Fact]
    public void ConfigureTimeSeriesStore_ReplacesDefaultRegistrationForRecordType()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        services.ConfigureTimeSeriesStore<PriceBarDatasetKey, Bar>(
            configurator => configurator.UseRowFormat());

        services.Count(d => d.ServiceType == typeof(ITimeSeriesStoreRegistration))
            .Should().Be(
                8,
                "the Bar row override must replace its default columnar registration instead of "
                + "opening a ninth engine whose last registration merely hides the first");
    }

    // F-L5-01 (RESOLVED path) — once the time-series tier is actually used (the registry is
    // resolved, e.g. UseTimeSeriesStore=true or a snapshot is acquired), the container owns and
    // disposes each store's inner LSM engine/WAL: MS DI disposes a factory-created singleton on
    // ServiceProvider disposal, but only AFTER it has realized it. Resolving the registry realizes
    // the ITimeSeriesStoreRegistration factories, which is what enters them into disposal tracking.
    // After provider disposal the engine's file handles/WAL streams are released, so any operation on
    // the resolved engine throws ObjectDisposedException.
    //
    // NOTE: this test exercises the *registry-resolved* path only (it force-resolves the registry to
    // obtain an engine handle to assert on). The complementary default-path guarantee — that in the
    // shipped UseTimeSeriesStore=false config, where the registry is NEVER resolved, no engine is
    // eagerly opened and thus none can leak — is pinned by
    // DefaultConfig_DisposeWithoutResolvingRegistry_OpensNoEngine below (F-L5R2-01).
    [Fact]
    public async Task ServiceProvider_Dispose_DisposesUnderlyingTimeSeriesEngine()
    {
        var services = new ServiceCollection();
        services.AddTimeSeriesStore<PriceBarDatasetKey, Bar>(b => b.WithSegmentRoot(_root));

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ITimeSeriesStoreRegistry>();
        var engine = registry.ResolveEngine<PriceBarDatasetKey, Bar>();

        await provider.DisposeAsync();

        var key = new TimeSeriesKey<PriceBarDatasetKey>(
            new PriceBarDatasetKey("tiingo", DataFrequency.Daily, "AGG", null),
            new DateOnly(2024, 1, 2));

        var act = async () => await engine.ContainsKeyAsync(key);

        await act.Should().ThrowAsync<ObjectDisposedException>(
            "disposing the ServiceProvider must release the engine's WAL/file handles");
    }

    // F-L5R2-01 (DEFAULT path) — the disposal fix must not be inert in the shipped-default config.
    // MS DI disposes a factory singleton only once it has REALIZED it (on first resolve); an
    // ITimeSeriesStoreRegistration is realized only when the registry drains it, which happens only
    // when the registry is resolved (UseTimeSeriesStore=true / snapshot). UseTimeSeriesStore defaults
    // FALSE, so the shipped-default consumer never resolves the registry — yet the kernel used to open
    // all 8 LSM engines/WALs EAGERLY at AddMarketDataKernel. Eager-open + realize-on-resolve disposal
    // is the leak: the engines are opened but the container never disposal-tracks them, so
    // provider.DisposeAsync() releases none of them (the eagerly-opened engines/WALs leak).
    //
    // The invariant this pins: in the default config, building and disposing the provider WITHOUT
    // resolving the registry must leave no eagerly-opened, never-disposed engine. Opening each store's
    // engine creates its {record-type} segment folder (the WAL ctor's side effect), so an untouched
    // segment root — no per-record folders — is the observable proof that no engine was opened and
    // hence none leaked. This test must NOT resolve the registry (that is exactly the masking step the
    // default consumer omits). RED on the pre-fix eager-open code (folders created at
    // AddMarketDataKernel); GREEN once opening is deferred to registry realization.
    [Fact]
    public async Task DefaultConfig_DisposeWithoutResolvingRegistry_OpensNoEngine()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        var provider = services.BuildServiceProvider();

        // Do NOT resolve ITimeSeriesStoreRegistry — the shipped UseTimeSeriesStore=false path never
        // does. Dispose the provider straight away, as a default consumer's host would on shutdown.
        await provider.DisposeAsync();

        // No engine was opened, so no {record-type} segment folder was created — nothing to leak.
        // Assert across a columnar (Bar) and a row (OptionQuote) default store to cover both paths.
        Directory.Exists(Path.Combine(_root, nameof(Bar))).Should().BeFalse(
            "in the shipped UseTimeSeriesStore=false config the columnar engine must not be opened "
            + "eagerly — an eagerly-opened engine the container never realizes is a leaked WAL/file handle");
        Directory.Exists(Path.Combine(_root, nameof(OptionQuote))).Should().BeFalse(
            "in the shipped UseTimeSeriesStore=false config the row engine must not be opened eagerly");
    }
}
