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
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Normalization.Normalizers;
using Boutquin.MarketData.Orchestration.Pipeline;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// Pipeline-options composition fidelity for <c>AddMarketDataKernel</c>. The composition-time
/// reads that drive flag-gated registration (<c>RegisterDefaultNormalizers</c>, the store-shaping
/// builders) and the runtime <see cref="IOptions{TOptions}"/> reads <c>DataPipeline</c> performs
/// must agree in every supported composition shape: a single call carrying
/// <c>configurePipeline</c>, a host <c>Configure&lt;DataPipelineOptions&gt;</c> registered before
/// the kernel, and a repeated <c>AddMarketDataKernel</c>.
/// </summary>
/// <remarks>
/// Before the fix these shapes dropped configuration in <b>both</b> directions: the kernel's own
/// <c>configurePipeline</c> never joined the <c>IConfigureOptions</c> chain whenever a host had
/// already registered one (so the values it set were invisible to every runtime
/// <see cref="IOptions{TOptions}"/> read), and a flag set through the documented options-binding
/// channel never reached the composition-time reads (so it registered nothing). Each test below
/// pins one direction; together they pin the invariant that the two surfaces observe the same
/// configuration.
/// </remarks>
public sealed class KernelPipelineOptionsCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "mktdata-optcompose-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Options channel → composition. A host that turns the flag on through the binding channel
    /// <c>DataPipelineOptions</c> documents — <c>services.Configure&lt;DataPipelineOptions&gt;</c>,
    /// which is also what <c>IConfiguration</c> section binding registers — before the kernel call
    /// gets the nine default normalizers actually registered. The composition-time read must
    /// resolve the registered <c>IConfigureOptions</c> chain, not a private instance fed only by
    /// the <c>configurePipeline</c> parameter.
    /// </summary>
    [Fact]
    public void HostConfiguresFlagOnBeforeKernel_RegistersDefaultNormalizers()
    {
        var services = new ServiceCollection();
        services.Configure<DataPipelineOptions>(o => o.RegisterDefaultNormalizers = true);

        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<DataPipelineOptions>>()
            .Value.RegisterDefaultNormalizers.Should().BeTrue(
                "the host set the flag through the documented options-binding channel");

        provider.GetService<INormalizer<Bar, Bar>>().Should().BeOfType<BarNormalizer>(
            "a flag set through the options-binding channel must drive the composition-time "
            + "registration block, not be silently inert");
    }

    /// <summary>
    /// Callback → runtime. The kernel's own <c>configurePipeline</c> delegate must reach the
    /// runtime <see cref="IOptions{TOptions}"/> instance even when a host has already registered
    /// an <c>IConfigureOptions&lt;DataPipelineOptions&gt;</c> of its own. Both the host's value and
    /// the kernel parameter's values survive — the kernel's composition-time decisions and
    /// <c>DataPipeline</c>'s runtime reads must not diverge.
    /// </summary>
    [Fact]
    public void HostConfiguresBeforeKernel_KernelConfigurePipelineStillReachesRuntimeOptions()
    {
        var services = new ServiceCollection();
        services.Configure<DataPipelineOptions>(o => o.ContinueOnPartitionFailure = true);

        services.AddMarketDataKernel(
            timeSeriesDirectory: _root,
            configurePipeline: o =>
            {
                o.UseTimeSeriesStore = true;
                o.L1CacheTtl = TimeSpan.FromMinutes(42);
            });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DataPipelineOptions>>().Value;

        options.UseTimeSeriesStore.Should().BeTrue(
            "a pre-existing host Configure must not suppress the kernel's own configurePipeline");
        options.L1CacheTtl.Should().Be(
            TimeSpan.FromMinutes(42),
            "the kernel parameter's values must reach the runtime options instance");
        options.ContinueOnPartitionFailure.Should().BeTrue(
            "the host's own Configure delegate is preserved alongside the kernel's");
    }

    /// <summary>
    /// Repeated composition. A second <c>AddMarketDataKernel</c> — a shape the kernel explicitly
    /// blesses for test fixtures composing multiple host builders — must not have its
    /// <c>configurePipeline</c> discarded. A host enabling the time-series tier on the second
    /// builder gets it at runtime rather than a silently-ignored option.
    /// </summary>
    [Fact]
    public void SecondKernelCall_ConfigurePipelineReachesRuntimeOptions()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        services.AddMarketDataKernel(
            timeSeriesDirectory: _root,
            configurePipeline: o => o.UseTimeSeriesStore = true);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<DataPipelineOptions>>()
            .Value.UseTimeSeriesStore.Should().BeTrue(
                "the second kernel call's delegate must join the options chain, not be dropped "
                + "because the first call already registered one");
    }

    /// <summary>
    /// Single-call regression guard: with no host <c>Configure</c> present at all, the kernel's
    /// <c>configurePipeline</c> still reaches both surfaces. This is the shape that already
    /// worked; it must keep working after the guard change.
    /// </summary>
    [Fact]
    public void SingleKernelCall_ConfigurePipelineReachesBothCompositionAndRuntimeOptions()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(
            timeSeriesDirectory: _root,
            configurePipeline: o =>
            {
                o.RegisterDefaultNormalizers = true;
                o.L1CacheTtl = TimeSpan.FromMinutes(7);
            });

        using var provider = services.BuildServiceProvider();

        // Composition-time surface: the flag drove the registration block.
        provider.GetService<INormalizer<Bar, Bar>>().Should().BeOfType<BarNormalizer>();

        // Runtime surface: the same values are visible through IOptions.
        var options = provider.GetRequiredService<IOptions<DataPipelineOptions>>().Value;
        options.RegisterDefaultNormalizers.Should().BeTrue();
        options.L1CacheTtl.Should().Be(TimeSpan.FromMinutes(7));
    }

    /// <summary>
    /// Unconfigured composition regression guard: with neither a host <c>Configure</c> nor a
    /// <c>configurePipeline</c>, <see cref="IOptions{TOptions}"/> must still resolve (the kernel's
    /// <c>DataPipeline</c> takes it as a constructor dependency) and carry the shipped defaults.
    /// </summary>
    [Fact]
    public void UnconfiguredKernel_StillResolvesOptionsWithShippedDefaults()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(timeSeriesDirectory: _root);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DataPipelineOptions>>().Value;

        options.RegisterDefaultNormalizers.Should().BeFalse();
        options.UseTimeSeriesStore.Should().BeFalse();
    }
}
