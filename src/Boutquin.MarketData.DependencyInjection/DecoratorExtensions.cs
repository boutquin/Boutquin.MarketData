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
using Microsoft.Extensions.DependencyInjection;

namespace Boutquin.MarketData.DependencyInjection;

/// <summary>
/// Extension methods for wrapping registered services with decorator layers
/// (e.g., L1 memory cache, L2 write-through, logging, metrics).
/// </summary>
/// <remarks>
/// <para>
/// The decorator pattern is the standard mechanism for composing caching, logging,
/// and resilience layers around data fetchers. This helper eliminates the boilerplate
/// of finding the existing registration, removing it, and re-registering with the decorator.
/// </para>
/// <para>
/// Adapted from Trading's <c>AddBoutquinTradingCaching</c> pattern where the same
/// find-remove-reregister logic was duplicated for each fetcher type.
/// </para>
/// </remarks>
public static class DecoratorExtensions
{
    /// <summary>
    /// Wraps an existing service registration with a decorator. The decorator factory
    /// receives the original (inner) service instance and the service provider.
    /// </summary>
    /// <typeparam name="TService">The service interface to decorate.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="decoratorFactory">
    /// Factory that takes the inner service and the service provider, and returns the decorated instance.
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// If no registration exists for <typeparamref name="TService"/>, this method is a no-op.
    /// The decorator inherits the lifetime (singleton, scoped, transient) of the original registration.
    /// The container retains disposal ownership of a type- or factory-created inner service;
    /// the decorator must not dispose it. Use the ownership overload for an owning decorator.
    /// An externally supplied instance remains caller-owned.
    /// </remarks>
    public static IServiceCollection Decorate<TService>(
        this IServiceCollection services,
        Func<TService, IServiceProvider, TService> decoratorFactory)
        where TService : class
        => services.Decorate(decoratorFactory, decoratorOwnsInner: false);

    /// <summary>Wraps a registration with an explicit inner-service disposal contract.</summary>
    /// <typeparam name="TService">The service interface to decorate.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="decoratorFactory">Creates a decorator around the original service.</param>
    /// <param name="decoratorOwnsInner">
    /// When true, a successfully created decorator assumes disposal of the inner service.
    /// When false, the container retains ownership of type/factory-created inner services;
    /// externally supplied instances remain caller-owned. A factory returning the inner instance
    /// directly leaves that instance's disposal with the container as the resolved service.
    /// </param>
    /// <returns>The service collection, preserving the original service lifetime.</returns>
    /// <remarks>
    /// An owning decorator must implement the required disposal interface and release its inner
    /// exactly once. Ownership transfers only after the decorator factory succeeds. For an
    /// externally supplied instance, choose true only when its caller explicitly transfers ownership.
    /// Dispose scopes/providers asynchronously when the inner service supports only asynchronous disposal.
    /// </remarks>
    public static IServiceCollection Decorate<TService>(
        this IServiceCollection services,
        Func<TService, IServiceProvider, TService> decoratorFactory,
        bool decoratorOwnsInner)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(decoratorFactory);

        var descriptor = services.LastOrDefault(
            d => d.ServiceType == typeof(TService) && !d.IsKeyedService);
        if (descriptor is null)
        {
            return services;
        }

        services.Remove(descriptor);
        var ownerKey = new object();
        services.Add(ServiceDescriptor.DescribeKeyed(
            typeof(InnerOwner<TService>), ownerKey,
            (sp, _) => new InnerOwner<TService>(
                ResolveFromDescriptor<TService>(descriptor, sp), descriptor.ImplementationInstance is null),
            descriptor.Lifetime));

        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            sp =>
            {
                var owner = sp.GetRequiredKeyedService<InnerOwner<TService>>(ownerKey);
                var decorated = decoratorFactory(owner.Value, sp);
                if (decoratorOwnsInner || ReferenceEquals(decorated, owner.Value))
                {
                    owner.ReleaseOwnership();
                }

                return decorated;
            },
            descriptor.Lifetime));

        return services;
    }

    private sealed class InnerOwner<TService>(TService value, bool owns) : IDisposable, IAsyncDisposable
        where TService : class
    {
        private int _owns = owns ? 1 : 0;
        public TService Value { get; } = value;
        public void ReleaseOwnership() => Interlocked.Exchange(ref _owns, 0);

        public void Dispose()
        {
            if (Volatile.Read(ref _owns) != 0 && Value is IAsyncDisposable && Value is not IDisposable)
            {
                throw new InvalidOperationException("The decorated inner service requires asynchronous scope/provider disposal.");
            }

            if (Interlocked.Exchange(ref _owns, 0) != 0)
            {
                (Value as IDisposable)?.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _owns, 0) == 0)
            {
                return;
            }

            if (Value is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                (Value as IDisposable)?.Dispose();
            }
        }
    }

    private static TService ResolveFromDescriptor<TService>(ServiceDescriptor descriptor, IServiceProvider sp)
        where TService : class
    {
        if (descriptor.ImplementationInstance is TService instance)
        {
            return instance;
        }

        if (descriptor.ImplementationFactory is not null)
        {
            return (TService)descriptor.ImplementationFactory(sp);
        }

        if (descriptor.ImplementationType is not null)
        {
            return (TService)ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType);
        }

        throw new InvalidOperationException(
            $"Cannot resolve inner service for decorator: {typeof(TService).Name}. " +
            "The original descriptor has no instance, factory, or implementation type.");
    }
}
