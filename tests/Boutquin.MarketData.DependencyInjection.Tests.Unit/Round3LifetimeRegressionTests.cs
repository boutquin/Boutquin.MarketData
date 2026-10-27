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

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

public sealed class Round3LifetimeRegressionTests
{
    [Theory]
    [InlineData(ServiceLifetime.Singleton, false)]
    [InlineData(ServiceLifetime.Singleton, true)]
    [InlineData(ServiceLifetime.Scoped, false)]
    [InlineData(ServiceLifetime.Scoped, true)]
    [InlineData(ServiceLifetime.Transient, false)]
    [InlineData(ServiceLifetime.Transient, true)]
    public void Decorated_inner_keeps_container_disposal(ServiceLifetime lifetime, bool factory)
    {
        var counter = new Counter();
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton(counter);
        services.Add(factory
            ? ServiceDescriptor.Describe(typeof(IService), _ => new DisposableService(counter), lifetime)
            : ServiceDescriptor.Describe(typeof(IService), typeof(DisposableService), lifetime));
        services.Decorate<IService>((inner, _) => new Wrapper(inner));
        var provider = services.BuildServiceProvider();
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IService>().Value.Should().Be(7);
        }

        counter.Disposals.Should().Be(lifetime == ServiceLifetime.Singleton ? 0 : 1);
        provider.Dispose();
        counter.Disposals.Should().Be(1, "the container still owns its created inner service");
    }

    [Fact]
    public void External_inner_remains_caller_owned()
    {
        var counter = new Counter();
        var inner = new DisposableService(counter);
        var services = new ServiceCollection();
        services.AddSingleton<IService>(inner);
        services.Decorate<IService>((service, _) => new Wrapper(service));
        using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredService<IService>().Value.Should().Be(7);
        }

        counter.Disposals.Should().Be(0);
        inner.Dispose();
        counter.Disposals.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Owning_or_identity_decorator_disposes_inner_once(bool identity)
    {
        var counter = new Counter();
        var services = new ServiceCollection();
        services.AddSingleton<IService>(_ => new DisposableService(counter));
        services.Decorate<IService>(
            (inner, _) => identity ? inner : new OwningWrapper(inner), decoratorOwnsInner: !identity);
        using (var provider = services.BuildServiceProvider())
        {
            provider.GetRequiredService<IService>().Value.Should().Be(7);
        }

        counter.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task Async_inner_is_disposed_through_chained_nonowning_wrappers()
    {
        var counter = new Counter();
        var services = new ServiceCollection();
        services.AddScoped<IService>(_ => new AsyncService(counter));
        services.Decorate<IService>((inner, _) => new Wrapper(inner));
        services.Decorate<IService>((inner, _) => new Wrapper(inner));
        await using (var provider = services.BuildServiceProvider())
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IService>().Value.Should().Be(7);
        }

        counter.Disposals.Should().Be(1);
    }

    private interface IService
    {
        int Value { get; }
    }

    private sealed class Counter
    {
        public int Disposals { get; set; }
    }

    private sealed class DisposableService(Counter counter) : IService, IDisposable
    {
        public int Value => 7;
        public void Dispose() => counter.Disposals++;
    }

    private sealed class Wrapper(IService inner) : IService
    {
        public int Value => inner.Value;
    }

    private sealed class OwningWrapper(IService inner) : IService, IDisposable
    {
        public int Value => inner.Value;
        public void Dispose() => ((IDisposable)inner).Dispose();
    }

    private sealed class AsyncService(Counter counter) : IService, IAsyncDisposable
    {
        public int Value => 7;
        public ValueTask DisposeAsync()
        {
            counter.Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
