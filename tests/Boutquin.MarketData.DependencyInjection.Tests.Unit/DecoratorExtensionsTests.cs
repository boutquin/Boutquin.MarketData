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

public sealed class DecoratorExtensionsTests
{
    [Fact]
    public void Decorate_wraps_existing_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGreeter>(new PlainGreeter());
        services.Decorate<IGreeter>((inner, _) => new ShoutingGreeter(inner));

        var provider = services.BuildServiceProvider();
        var greeter = provider.GetRequiredService<IGreeter>();

        greeter.Greet("world").Should().Be("HELLO, WORLD!");
    }

    [Fact]
    public void Decorate_is_noop_when_service_not_registered()
    {
        var services = new ServiceCollection();
        services.Decorate<IGreeter>((inner, _) => new ShoutingGreeter(inner));

        services.Should().BeEmpty();
    }

    [Fact]
    public void Decorate_preserves_original_lifetime()
    {
        var services = new ServiceCollection();
        services.AddScoped<IGreeter, PlainGreeter>();
        services.Decorate<IGreeter>((inner, _) => new ShoutingGreeter(inner));

        var descriptor = services.Single(d => d.ServiceType == typeof(IGreeter));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void Decorate_can_chain_multiple_decorators()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGreeter>(new PlainGreeter());
        services.Decorate<IGreeter>((inner, _) => new PrefixGreeter("[1]", inner));
        services.Decorate<IGreeter>((inner, _) => new PrefixGreeter("[2]", inner));

        var provider = services.BuildServiceProvider();
        var greeter = provider.GetRequiredService<IGreeter>();

        greeter.Greet("x").Should().Be("[2] [1] Hello, x!");
    }

    [Fact]
    public void Decorate_preserves_keyed_registration_and_wraps_unkeyed_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGreeter>(new PlainGreeter());
        services.AddKeyedSingleton<IGreeter>("quiet", new PrefixGreeter("[keyed]", new PlainGreeter()));

        services.Decorate<IGreeter>((inner, _) => new ShoutingGreeter(inner));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IGreeter>().Greet("world").Should().Be("HELLO, WORLD!");
        provider.GetRequiredKeyedService<IGreeter>("quiet").Greet("world")
            .Should().Be("[keyed] Hello, world!");
    }

    // --- Test helpers ---

    private interface IGreeter
    {
        string Greet(string name);
    }

    private sealed class PlainGreeter : IGreeter
    {
        public string Greet(string name) => $"Hello, {name}!";
    }

    private sealed class ShoutingGreeter(IGreeter inner) : IGreeter
    {
        public string Greet(string name) => inner.Greet(name).ToUpperInvariant();
    }

    private sealed class PrefixGreeter(string prefix, IGreeter inner) : IGreeter
    {
        public string Greet(string name) => $"{prefix} {inner.Greet(name)}";
    }
}
