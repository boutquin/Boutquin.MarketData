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
using System.Runtime.CompilerServices;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.Memory;
using Boutquin.MarketData.Cache.TimeSeries;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit;

public sealed class RcCacheOwnershipRegressionTests
{
    [Fact]
    public async Task Unrelated_activity_releases_expired_payloads_and_preserves_active_entries()
    {
        var clock = new ControlledTimeProvider();
        var cache = new ExactOnceMemoryCache(clock);
        var expired = AddCollectiblePayload(cache);
        await cache.SetAsync("active", "retained", TimeSpan.FromHours(1));
        clock.Advance(TimeSpan.FromMinutes(2));

        (await cache.GetAsync<object>("unrelated-miss")).Should().BeNull();
        ForceCollection();

        expired.IsAlive.Should().BeFalse("expired one-use identities must release their payloads during unrelated activity");
        (await cache.GetAsync<string>("active")).Should().Be("retained");
        GC.KeepAlive(cache);
    }

    [Fact]
    public async Task Expired_read_does_not_remove_a_replacement_installed_during_expiry_check()
    {
        var clock = new ControlledTimeProvider();
        var cache = new ExactOnceMemoryCache(clock);
        await cache.SetAsync("key", "expired", TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromSeconds(2));
        clock.OnNextRead = () => cache.SetAsync("key", "fresh", TimeSpan.FromHours(1)).GetAwaiter().GetResult();

        (await cache.GetAsync<string>("key")).Should().BeNull();

        (await cache.GetAsync<string>("key")).Should().Be("fresh", "expiry reclaim must remove only the entry it observed");
    }

    [Fact]
    public async Task Unrelated_write_also_releases_expired_payloads()
    {
        var clock = new ControlledTimeProvider();
        var cache = new ExactOnceMemoryCache(clock);
        var expired = AddCollectiblePayload(cache);
        clock.Advance(TimeSpan.FromMinutes(2));

        await cache.SetAsync("new-key", "new-value", TimeSpan.FromHours(1));
        ForceCollection();

        expired.IsAlive.Should().BeFalse();
        (await cache.GetAsync<string>("new-key")).Should().Be("new-value");
        GC.KeepAlive(cache);
    }

    [Fact]
    public async Task Cancelled_operations_do_not_read_or_replace_cache_values()
    {
        var cache = new ExactOnceMemoryCache(new ControlledTimeProvider());
        await cache.SetAsync("key", "original", TimeSpan.FromMinutes(1));
        var cancelled = new CancellationToken(canceled: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync<string>("key", cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.SetAsync("key", "changed", TimeSpan.FromMinutes(1), cancelled));
        (await cache.GetAsync<string>("key")).Should().Be("original");
    }

    [Fact]
    public void Catalogs_own_registration_lists_and_refuse_getter_mutation()
    {
        Type builderType = typeof(IDatasetKeyBuilder<PriceHistoryRequest, PriceBarDatasetKey>);
        Type fanOutType = typeof(IDatasetKeyFanOut<YieldCurveQuoteRequest, YieldCurveQuoteDatasetKey,
            Boutquin.MarketData.Abstractions.Records.YieldCurveQuote, Boutquin.MarketData.Abstractions.Records.YieldCurveQuote>);
        var builders = new List<Type> { builderType };
        var fanOuts = new List<Type> { fanOutType };
        var builderCatalog = new DatasetKeyBuilderCatalog(builders);
        var fanOutCatalog = new DatasetKeyFanOutCatalog(fanOuts);
        builders.Clear();
        fanOuts.Clear();

        builderCatalog.RegisteredBuilderInterfaces.Should().Equal(builderType);
        fanOutCatalog.RegisteredFanOutInterfaces.Should().Equal(fanOutType);
        Action mutateBuilders = () => ((IList<Type>)builderCatalog.RegisteredBuilderInterfaces).Clear();
        Action mutateFanOuts = () => ((IList<Type>)fanOutCatalog.RegisteredFanOutInterfaces).Clear();
        mutateBuilders.Should().Throw<NotSupportedException>();
        mutateFanOuts.Should().Throw<NotSupportedException>();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddCollectiblePayload(ExactOnceMemoryCache cache)
    {
        var payload = new byte[1024 * 1024];
        cache.SetAsync("one-use", payload, TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();
        return new WeakReference(payload);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class ControlledTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public Action? OnNextRead { get; set; }
        public override DateTimeOffset GetUtcNow()
        {
            var callback = OnNextRead;
            OnNextRead = null;
            callback?.Invoke();
            return _now;
        }
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
