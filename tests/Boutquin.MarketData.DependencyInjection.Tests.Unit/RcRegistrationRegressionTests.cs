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

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Cache.TimeSeries;
using Boutquin.Storage.Domain.Enums;
using Boutquin.Storage.Domain.Interfaces;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;
using Boutquin.Storage.Domain.ValueObjects.TimeSeries;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

public sealed class RcRegistrationRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decorator_null_factory_does_not_mutate_registrations(bool ownershipOverload)
    {
        var services = new ServiceCollection();
        services.AddSingleton<object>(new object());
        var before = services.ToArray();
        Action decorate = () =>
        {
            if (ownershipOverload)
            {
                services.Decorate<object>(null!, decoratorOwnsInner: true);
            }
            else
            {
                services.Decorate<object>(null!);
            }
        };
        decorate.Should().Throw<ArgumentNullException>().WithParameterName("decoratorFactory");
        services.Should().Equal(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decorator_null_services_fails_at_boundary(bool ownershipOverload)
    {
        IServiceCollection services = null!;
        Action decorate = () =>
        {
            if (ownershipOverload)
            {
                services.Decorate<object>((inner, _) => inner, decoratorOwnsInner: true);
            }
            else
            {
                services.Decorate<object>((inner, _) => inner);
            }
        };
        decorate.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Disposed_registration_rejects_apply_without_mutating_registry(bool row, bool asyncDispose)
    {
        var (registration, owner) = CreateRegistration(row);
        if (asyncDispose)
        {
            await registration.DisposeAsync();
        }
        else
        {
            registration.Dispose();
        }

        var registry = new DefaultTimeSeriesStoreRegistry();
        Action apply = () => registration.ApplyTo(registry);
        apply.Should().Throw<ObjectDisposedException>();
        registry.Resolve<PriceBarDatasetKey, Bar>().Should().BeNull();
        registry.ResolveScanEngine<PriceBarDatasetKey, Bar>().Should().BeNull();
        owner.Disposals.Should().Be(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Concurrent_sync_and_async_disposal_claim_the_engine_once(bool row, bool asyncFirst)
    {
        var (registration, owner) = CreateRegistration(row);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        owner.OnDispose = () =>
        {
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
        };
        var first = Task.Run(async () =>
        {
            if (asyncFirst)
            {
                await registration.DisposeAsync();
            }
            else
            {
                registration.Dispose();
            }
        });
        try
        {
            entered.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
            var second = Task.Run(async () =>
            {
                if (asyncFirst)
                {
                    registration.Dispose();
                }
                else
                {
                    await registration.DisposeAsync();
                }
            });
            await second.WaitAsync(TimeSpan.FromSeconds(10));
            owner.Disposals.Should().Be(1);
        }
        finally
        {
            release.Set();
            await first.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await registration.DisposeAsync();
        registration.Dispose();
        owner.Disposals.Should().Be(1);
    }

    [Fact]
    public void Undefined_segment_format_is_rejected_before_store_registration()
    {
        var services = new ServiceCollection();
        Action compose = () => services.AddMarketDataKernel(configurePipeline: options =>
            options.TimeSeriesSegmentFormat = (TimeSeriesSegmentFormat)999);
        compose.Should().Throw<ArgumentOutOfRangeException>();
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(ITimeSeriesStoreRegistration));
    }

    private static (ITimeSeriesStoreRegistration Registration, CountingProxy Owner) CreateRegistration(bool row)
    {
        var engine = DispatchProxy.Create<ILsmStorageEngine<TimeSeriesKey<PriceBarDatasetKey>, Bar>, CountingProxy>();
        var store = DispatchProxy.Create<ITimeSeriesStore<PriceBarDatasetKey, Bar>, CountingProxy>();
        ITimeSeriesStoreRegistration registration = row
            ? new RowTimeSeriesStoreRegistration<PriceBarDatasetKey, Bar>(store, engine)
            : new TimeSeriesStoreRegistration<PriceBarDatasetKey, Bar>(store, engine);
        return (registration, (CountingProxy)(object)engine);
    }

    public class CountingProxy : DispatchProxy, IDisposable, IAsyncDisposable
    {
        private int _disposals;
        public int Disposals => Volatile.Read(ref _disposals);
        public Action? OnDispose { get; set; }
        public void Dispose()
        {
            Interlocked.Increment(ref _disposals);
            OnDispose?.Invoke();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => throw new InvalidOperationException($"Unexpected storage operation: {targetMethod?.Name}");
    }
}
