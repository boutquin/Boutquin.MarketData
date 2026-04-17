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
    /// </remarks>
    public static IServiceCollection Decorate<TService>(
        this IServiceCollection services,
        Func<TService, IServiceProvider, TService> decoratorFactory)
        where TService : class
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(TService));
        if (descriptor is null)
        {
            return services;
        }

        services.Remove(descriptor);

        services.Add(ServiceDescriptor.Describe(
            typeof(TService),
            sp =>
            {
                var inner = ResolveFromDescriptor<TService>(descriptor, sp);
                return decoratorFactory(inner, sp);
            },
            descriptor.Lifetime));

        return services;
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
