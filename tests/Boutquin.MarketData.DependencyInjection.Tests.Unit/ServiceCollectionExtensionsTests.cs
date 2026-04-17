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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Cache.Policies;
using Boutquin.MarketData.Orchestration.Coverage;
using Boutquin.MarketData.Orchestration.Pipeline;
using Boutquin.MarketData.Storage.Checkpointing;
using Boutquin.MarketData.Storage.Events;
using Boutquin.Storage.Domain.Interfaces.Schemas;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMarketDataKernel_registers_IDataPipeline()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddMarketDataKernel();
        var provider = services.BuildServiceProvider();

        // Act
        var pipeline = provider.GetService<IDataPipeline>();

        // Assert
        pipeline.Should().NotBeNull();
        pipeline.Should().BeOfType<DataPipeline>();
    }

    [Fact]
    public void AddMarketDataKernel_registers_SnapshotStalenessPolicy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddMarketDataKernel();
        var provider = services.BuildServiceProvider();

        // Act
        var policy = provider.GetService<SnapshotStalenessPolicy>();

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void AddMarketDataKernel_registers_CoverageCalculator()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddMarketDataKernel();
        var provider = services.BuildServiceProvider();

        // Act
        var calculator = provider.GetService<CoverageCalculator>();

        // Assert
        calculator.Should().NotBeNull();
    }

    [Fact]
    public void AddMarketDataKernel_configures_pipeline_options()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddMarketDataKernel(configurePipeline: opts =>
        {
            opts.L1CacheTtl = TimeSpan.FromMinutes(30);
            opts.ContinueOnPartitionFailure = false;
        });
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<DataPipelineOptions>>().Value;

        // Assert
        options.L1CacheTtl.Should().Be(TimeSpan.FromMinutes(30));
        options.ContinueOnPartitionFailure.Should().BeFalse();
    }

    [Fact]
    public void AddMarketDataKernel_registers_IClock()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddMarketDataKernel();
        var provider = services.BuildServiceProvider();

        // Act
        var clock = provider.GetService<IClock>();

        // Assert
        clock.Should().NotBeNull();
    }

    [Fact]
    public void AddMarketDataKernel_registers_ILocalCache()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddMarketDataKernel();
        var provider = services.BuildServiceProvider();

        // Act
        var cache = provider.GetService<ILocalCache>();

        // Assert
        cache.Should().NotBeNull();
    }

    [Fact]
    public void AddMarketDataKernel_registers_ISnapshotStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddMarketDataKernel();
        var provider = services.BuildServiceProvider();

        // Act
        var store = provider.GetService<ISnapshotStore>();

        // Assert
        store.Should().NotBeNull();
    }

    [Fact]
    public void AddMarketDataKernel_registers_IRawDocumentStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddMarketDataKernel();
        var provider = services.BuildServiceProvider();

        // Act
        var store = provider.GetService<IRawDocumentStore>();

        // Assert
        store.Should().NotBeNull();
    }

    [Fact]
    public void AddMarketDataKernel_registers_IngestionCheckpointStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddMarketDataKernel();
        var provider = services.BuildServiceProvider();

        // Act
        var store = provider.GetService<IngestionCheckpointStore>();

        // Assert
        store.Should().NotBeNull();
    }

    [Fact]
    public void AddMarketDataKernel_registers_IngestionEventLog()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddMarketDataKernel();
        var provider = services.BuildServiceProvider();

        // Act
        var log = provider.GetService<IngestionEventLog>();

        // Assert
        log.Should().NotBeNull();
    }

    [Fact]
    public void AddMarketDataKernel_registers_ISchemaRegistry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddMarketDataKernel();
        var provider = services.BuildServiceProvider();

        // Act
        var registry = provider.GetService<ISchemaRegistry>();

        // Assert
        registry.Should().NotBeNull();
    }

    [Fact]
    public void AddMarketDataKernel_TryAdd_does_not_overwrite_preregistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddSingleton<ILocalCache, FakeLocalCache>();
        services.AddMarketDataKernel();
        var provider = services.BuildServiceProvider();

        // Act
        var cache = provider.GetService<ILocalCache>();

        // Assert
        cache.Should().NotBeNull();
        cache.Should().BeOfType<FakeLocalCache>();
    }

    private sealed class FakeLocalCache : ILocalCache
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
            => Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
