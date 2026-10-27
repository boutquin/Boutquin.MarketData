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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// Kernel default-normalizer registration (feature-kernel-normalizer-registration.md M-1,
/// AC-1.1 … AC-1.8). Every assertion drives the <b>production</b> composition entry point
/// <c>AddMarketDataKernel</c> — never a hand-rolled service collection that duplicates the
/// registration block — so a block that were never reached by the real composition path would
/// fail these tests rather than pass them.
/// </summary>
public sealed class NormalizerRegistrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "mktdata-normreg-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Builds a container through the real <c>AddMarketDataKernel</c> composition path.
    /// </summary>
    private ServiceProvider BuildKernel(bool registerDefaultNormalizers)
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(
            timeSeriesDirectory: _root,
            configurePipeline: o => o.RegisterDefaultNormalizers = registerDefaultNormalizers);
        return services.BuildServiceProvider();
    }

    // ---------------------------------------------------------------- AC-1.1

    /// <summary>
    /// AC-1.1 — the option exists and defaults to <see langword="false"/>, so an existing
    /// consumer that never touches the flag composes exactly as it does today (§2.1).
    /// </summary>
    [Fact]
    public void RegisterDefaultNormalizers_DefaultsToFalse()
        => new DataPipelineOptions().RegisterDefaultNormalizers.Should().BeFalse();

    // ---------------------------------------------------------------- AC-1.2

    /// <summary>
    /// AC-1.2 — default path regression guard: with the options callback omitted entirely,
    /// no normalizer is resolvable for any record type (byte-identical composition to today).
    /// </summary>
    [Fact]
    public void FlagUnconfigured_ResolvesNoNormalizerForAnyRecordType()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(timeSeriesDirectory: _root);
        using var provider = services.BuildServiceProvider();

        AssertNoNormalizersResolve(provider);
    }

    /// <summary>
    /// AC-1.2 — explicit <see langword="false"/> is equivalent to unconfigured.
    /// </summary>
    [Fact]
    public void FlagOff_ResolvesNoNormalizerForAnyRecordType()
    {
        using var provider = BuildKernel(registerDefaultNormalizers: false);

        AssertNoNormalizersResolve(provider);
    }

    private static void AssertNoNormalizersResolve(IServiceProvider provider)
    {
        provider.GetService<INormalizer<Bar, Bar>>().Should().BeNull();
        provider.GetService<INormalizer<CorporateAction, CorporateAction>>().Should().BeNull();
        provider.GetService<INormalizer<DividendPayment, DividendPayment>>().Should().BeNull();
        provider.GetService<INormalizer<FactorObservation, FactorObservation>>().Should().BeNull();
        provider.GetService<INormalizer<FuturesSettlement, FuturesSettlement>>().Should().BeNull();
        provider.GetService<INormalizer<FxRate, FxRate>>().Should().BeNull();
        provider.GetService<INormalizer<OptionQuote, OptionQuote>>().Should().BeNull();
        provider.GetService<INormalizer<ScalarObservation, ScalarObservation>>().Should().BeNull();
        provider.GetService<INormalizer<YieldCurveQuote, YieldCurveQuote>>().Should().BeNull();
        provider.GetService<INormalizer<InstrumentMetadata, InstrumentMetadata>>().Should().BeNull();
    }

    // ---------------------------------------------------------------- AC-1.3

    /// <summary>
    /// AC-1.3 — with the flag on, each of the nine shipped record types resolves a non-null
    /// normalizer of the shipped concrete type.
    /// </summary>
    [Fact]
    public void FlagOn_ResolvesShippedNormalizerForEachOfTheNineRecordTypes()
    {
        using var provider = BuildKernel(registerDefaultNormalizers: true);

        provider.GetService<INormalizer<Bar, Bar>>()
            .Should().BeOfType<BarNormalizer>();
        provider.GetService<INormalizer<CorporateAction, CorporateAction>>()
            .Should().BeOfType<CorporateActionNormalizer>();
        provider.GetService<INormalizer<DividendPayment, DividendPayment>>()
            .Should().BeOfType<DividendPaymentNormalizer>();
        provider.GetService<INormalizer<FactorObservation, FactorObservation>>()
            .Should().BeOfType<FactorObservationNormalizer>();
        provider.GetService<INormalizer<FuturesSettlement, FuturesSettlement>>()
            .Should().BeOfType<FuturesSettlementNormalizer>();
        provider.GetService<INormalizer<FxRate, FxRate>>()
            .Should().BeOfType<FxRateNormalizer>();
        provider.GetService<INormalizer<OptionQuote, OptionQuote>>()
            .Should().BeOfType<OptionQuoteNormalizer>();
        provider.GetService<INormalizer<ScalarObservation, ScalarObservation>>()
            .Should().BeOfType<ScalarObservationNormalizer>();
        provider.GetService<INormalizer<YieldCurveQuote, YieldCurveQuote>>()
            .Should().BeOfType<YieldCurveQuoteNormalizer>();
    }

    /// <summary>
    /// AC-1.3 — <c>InstrumentMetadata</c> ships no normalizer, so it resolves
    /// <see langword="null"/> even with the flag on (§0 asymmetry).
    /// </summary>
    [Fact]
    public void FlagOn_ResolvesNoNormalizerForInstrumentMetadata()
    {
        using var provider = BuildKernel(registerDefaultNormalizers: true);

        provider.GetService<INormalizer<InstrumentMetadata, InstrumentMetadata>>()
            .Should().BeNull();
    }

    // ---------------------------------------------------------------- AC-1.4

    /// <summary>
    /// AC-1.4 drift guard (§2.6) — the <c>INormalizer&lt;T, T&gt;</c> implementations exported by
    /// the Normalization assembly and the record types the flag-on kernel registers are the same
    /// set, exactly. A tenth normalizer added to the package without a registration line fails
    /// here, and the failure message names the §2.2 statelessness precondition so the contributor
    /// is told what to check before adding the line.
    /// </summary>
    [Fact]
    public void FlagOn_RegisteredRecordTypes_MatchTheShippedNormalizerImplementationsExactly()
    {
        var implemented = typeof(BarNormalizer).Assembly
            .GetExportedTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(INormalizer<,>))
            .Select(i => i.GetGenericArguments())
            .Where(args => args[0] == args[1])
            .Select(args => args[0])
            .Distinct()
            .ToArray();

        implemented.Should().HaveCount(9, "the Normalization package ships nine normalizers");

        var services = new ServiceCollection();
        services.AddMarketDataKernel(
            timeSeriesDirectory: _root,
            configurePipeline: o => o.RegisterDefaultNormalizers = true);

        var registered = services
            .Where(d => d.ServiceType.IsGenericType
                && d.ServiceType.GetGenericTypeDefinition() == typeof(INormalizer<,>))
            .Select(d => d.ServiceType.GetGenericArguments()[0])
            .Distinct()
            .ToArray();

        registered.Should().BeEquivalentTo(
            implemented,
            "the flag-on registration block in AddMarketDataKernel must cover exactly the "
            + "INormalizer<T,T> implementations shipped by Boutquin.MarketData.Normalization — no "
            + "more, no fewer. If you added a normalizer, add its TryAddSingleton line to that "
            + "block, but FIRST check the §2.2 STATELESSNESS PRECONDITION: the block registers "
            + "SINGLETONS, which is safe only because every shipped normalizer is stateless across "
            + "Normalize calls (readonly instance fields only; the one static collection is "
            + "read-only at use). A normalizer carrying mutable per-call state — a buffer, an "
            + "accumulator, a cached last input — becomes a data race the moment it joins this "
            + "block as a shared singleton. Make it stateless, or register it with a non-singleton "
            + "lifetime and record the asymmetry in the block's comment; never add the line and "
            + "leave the mutable state in place");

        // Every registered record type must also RESOLVE — a descriptor that throws on
        // construction would satisfy a descriptor-only comparison while leaving the stage dead.
        using var provider = services.BuildServiceProvider();
        foreach (var recordType in registered)
        {
            provider.GetService(typeof(INormalizer<,>).MakeGenericType(recordType, recordType))
                .Should().NotBeNull($"INormalizer<{recordType.Name},{recordType.Name}> must resolve");
        }
    }

    // ---------------------------------------------------------------- AC-1.5

    /// <summary>
    /// AC-1.5 — a host normalizer registered <b>before</b> <c>AddMarketDataKernel</c> wins:
    /// <c>TryAddSingleton</c> yields to the existing registration (§2.3).
    /// </summary>
    [Fact]
    public void HostNormalizerRegisteredBeforeKernel_WinsOverKernelDefault()
    {
        var host = new HostBarNormalizer();
        var services = new ServiceCollection();
        services.AddSingleton<INormalizer<Bar, Bar>>(host);

        services.AddMarketDataKernel(
            timeSeriesDirectory: _root,
            configurePipeline: o => o.RegisterDefaultNormalizers = true);

        using var provider = services.BuildServiceProvider();
        provider.GetService<INormalizer<Bar, Bar>>().Should().BeSameAs(host);
    }

    /// <summary>
    /// AC-1.5 — a host normalizer registered <b>after</b> <c>AddMarketDataKernel</c> wins:
    /// <c>GetService</c> resolves the last registration (§2.3).
    /// </summary>
    [Fact]
    public void HostNormalizerRegisteredAfterKernel_WinsOverKernelDefault()
    {
        var host = new HostBarNormalizer();
        var services = new ServiceCollection();
        services.AddMarketDataKernel(
            timeSeriesDirectory: _root,
            configurePipeline: o => o.RegisterDefaultNormalizers = true);

        services.AddSingleton<INormalizer<Bar, Bar>>(host);

        using var provider = services.BuildServiceProvider();
        provider.GetService<INormalizer<Bar, Bar>>().Should().BeSameAs(host);
    }

    /// <summary>
    /// AC-1.5 — <c>RemoveAll</c> is the per-record opt-out: it leaves that one record type
    /// unresolvable while the other eight remain registered (§2.3).
    /// </summary>
    [Fact]
    public void RemoveAllOptOut_LeavesOnlyThatRecordTypeUnresolved()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(
            timeSeriesDirectory: _root,
            configurePipeline: o => o.RegisterDefaultNormalizers = true);

        services.RemoveAll<INormalizer<Bar, Bar>>();

        using var provider = services.BuildServiceProvider();

        provider.GetService<INormalizer<Bar, Bar>>().Should().BeNull();

        provider.GetService<INormalizer<CorporateAction, CorporateAction>>().Should().NotBeNull();
        provider.GetService<INormalizer<DividendPayment, DividendPayment>>().Should().NotBeNull();
        provider.GetService<INormalizer<FactorObservation, FactorObservation>>().Should().NotBeNull();
        provider.GetService<INormalizer<FuturesSettlement, FuturesSettlement>>().Should().NotBeNull();
        provider.GetService<INormalizer<FxRate, FxRate>>().Should().NotBeNull();
        provider.GetService<INormalizer<OptionQuote, OptionQuote>>().Should().NotBeNull();
        provider.GetService<INormalizer<ScalarObservation, ScalarObservation>>().Should().NotBeNull();
        provider.GetService<INormalizer<YieldCurveQuote, YieldCurveQuote>>().Should().NotBeNull();
    }

    // ---------------------------------------------------------------- AC-1.7

    /// <summary>
    /// AC-1.7 — repeated composition idempotency, asserted on the <b>descriptor count</b>, not on
    /// <c>GetService</c> being non-null (which passes even with duplicates). The marker guard that
    /// protects the time-series stores exists because a repeated kernel call once silently
    /// double-opened LSM engines over one folder, so "idempotent by construction" is exactly the
    /// reasoning this repo tests rather than trusts (§2.2).
    /// </summary>
    [Fact]
    public void RepeatedKernelComposition_YieldsExactlyOneDescriptorPerRecordType()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(
            timeSeriesDirectory: _root,
            configurePipeline: o => o.RegisterDefaultNormalizers = true);
        services.AddMarketDataKernel(
            timeSeriesDirectory: _root,
            configurePipeline: o => o.RegisterDefaultNormalizers = true);

        services.Count(d => d.ServiceType == typeof(INormalizer<Bar, Bar>)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(INormalizer<CorporateAction, CorporateAction>)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(INormalizer<DividendPayment, DividendPayment>)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(INormalizer<FactorObservation, FactorObservation>)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(INormalizer<FuturesSettlement, FuturesSettlement>)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(INormalizer<FxRate, FxRate>)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(INormalizer<OptionQuote, OptionQuote>)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(INormalizer<ScalarObservation, ScalarObservation>)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(INormalizer<YieldCurveQuote, YieldCurveQuote>)).Should().Be(1);
    }

    /// <summary>
    /// Host-supplied normalizer double, standing in for a downstream repo's own
    /// <c>INormalizer&lt;Bar, Bar&gt;</c>.
    /// </summary>
    private sealed class HostBarNormalizer : INormalizer<Bar, Bar>
    {
        public Bar Normalize(Bar raw) => raw;
    }

    // ---------------------------------------------------------------- AC-1.8

    /// <summary>
    /// Composes logging exactly the way a production host does — a provider registered through
    /// the logging builder, <c>services.AddLogging(b => b.AddProvider(...))</c> — then builds the
    /// container and materializes <see cref="IDataPipeline"/>. This is AC-1.8's <b>primary</b>
    /// shape: the round-1 implementation read <see cref="ILoggerFactory"/> off the service
    /// collection's descriptors, which this shape (implementation-type registration) is invisible
    /// to, so the notice was silent in every real host.
    /// </summary>
    private ServiceProvider BuildKernelWithAddLogging(
        bool registerDefaultNormalizers,
        CapturingLoggerProvider sink)
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(sink);
        });

        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_root, "snapshots"),
            rawDocumentDirectory: Path.Combine(_root, "raw"),
            checkpointDirectory: Path.Combine(_root, "checkpoints"),
            eventLogDirectory: Path.Combine(_root, "events"),
            timeSeriesDirectory: Path.Combine(_root, "ts"),
            configurePipeline: o => o.RegisterDefaultNormalizers = registerDefaultNormalizers);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// AC-1.8 <b>primary</b> (§2.4) — an <c>AddLogging(...)</c>-composed host observes the flag-on
    /// <c>Information</c> registration event naming the record-type count. The on-path is not
    /// silent for the composition shape real hosts actually build.
    /// </summary>
    [Fact]
    public void FlagOn_UnderAddLogging_LogsInformationRegistrationEventNamingTheCount()
    {
        var sink = new CapturingLoggerProvider();
        using var provider = BuildKernelWithAddLogging(
            registerDefaultNormalizers: true, sink);

        _ = provider.GetRequiredService<IDataPipeline>();

        sink.Entries.Should().ContainSingle(e =>
            e.Category == s_kernelLogCategory
            && e.Level == LogLevel.Information
            && e.Message.Contains("9 default normalizer"));
    }

    /// <summary>
    /// AC-1.8 <b>primary</b> (§2.4) — the same <c>AddLogging(...)</c> host observes the flag-off
    /// <c>Debug</c> pass-through notice. The off-path — the supported default — is not silent
    /// either; it is <c>Debug</c> rather than <c>Warning</c> so hosts are not trained to ignore a
    /// warning on the default path.
    /// </summary>
    [Fact]
    public void FlagOff_UnderAddLogging_LogsDebugPassThroughNotice()
    {
        var sink = new CapturingLoggerProvider();
        using var provider = BuildKernelWithAddLogging(
            registerDefaultNormalizers: false, sink);

        _ = provider.GetRequiredService<IDataPipeline>();

        sink.Entries.Should().ContainSingle(e =>
            e.Category == s_kernelLogCategory
            && e.Level == LogLevel.Debug
            && e.Message.Contains("RegisterDefaultNormalizers = false")
            && e.Message.Contains("un-normalized"));
    }

    /// <summary>
    /// AC-1.8 <b>primary</b> (§2.4) — the two branches are mutually exclusive under the real-host
    /// shape: the on-path emits no pass-through notice and the off-path emits no registration
    /// event. A guard written on one branch but not its sibling would satisfy neither pairing.
    /// </summary>
    [Fact]
    public void CompositionLogging_EmitsExactlyOneBranchPerComposition()
    {
        var onSink = new CapturingLoggerProvider();
        using var onProvider = BuildKernelWithAddLogging(
            registerDefaultNormalizers: true, onSink);
        _ = onProvider.GetRequiredService<IDataPipeline>();

        var offSink = new CapturingLoggerProvider();
        using var offProvider = BuildKernelWithAddLogging(
            registerDefaultNormalizers: false, offSink);
        _ = offProvider.GetRequiredService<IDataPipeline>();

        // Non-vacuous in both directions: each side must have emitted its OWN branch before the
        // absence of the sibling branch means anything.
        onSink.Entries.Should().Contain(e =>
            e.Category == s_kernelLogCategory && e.Level == LogLevel.Information);
        onSink.Entries.Should().NotContain(e =>
            e.Category == s_kernelLogCategory && e.Level == LogLevel.Debug);

        offSink.Entries.Should().Contain(e =>
            e.Category == s_kernelLogCategory && e.Level == LogLevel.Debug);
        offSink.Entries.Should().NotContain(e =>
            e.Category == s_kernelLogCategory && e.Level == LogLevel.Information);
    }

    /// <summary>
    /// AC-1.8 <b>regression guard</b> (R2) — a host that registers a logger factory <b>instance</b>
    /// (the one shape the round-1 descriptor scan supported) must keep seeing the notice. Deferred
    /// provider resolution is a strict superset of the descriptor scan, but that is proven here
    /// rather than assumed.
    /// </summary>
    [Fact]
    public void FlagOn_UnderInstanceRegisteredLoggerFactory_StillLogsInformationEvent()
    {
        var factory = new CapturingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(factory);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_root, "snapshots"),
            rawDocumentDirectory: Path.Combine(_root, "raw"),
            checkpointDirectory: Path.Combine(_root, "checkpoints"),
            eventLogDirectory: Path.Combine(_root, "events"),
            timeSeriesDirectory: Path.Combine(_root, "ts"),
            configurePipeline: o => o.RegisterDefaultNormalizers = true);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IDataPipeline>();

        factory.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Information && e.Message.Contains("9 default normalizer"));
    }

    /// <summary>
    /// AC-1.8 <b>regression guard</b> (R2) — the flag-off branch under the same
    /// instance-registered shape.
    /// </summary>
    [Fact]
    public void FlagOff_UnderInstanceRegisteredLoggerFactory_StillLogsDebugNotice()
    {
        var factory = new CapturingLoggerFactory();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(factory);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_root, "snapshots"),
            rawDocumentDirectory: Path.Combine(_root, "raw"),
            checkpointDirectory: Path.Combine(_root, "checkpoints"),
            eventLogDirectory: Path.Combine(_root, "events"),
            timeSeriesDirectory: Path.Combine(_root, "ts"),
            configurePipeline: o => o.RegisterDefaultNormalizers = false);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IDataPipeline>();

        factory.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Debug
            && e.Message.Contains("RegisterDefaultNormalizers = false")
            && e.Message.Contains("un-normalized"));
    }

    /// <summary>
    /// Builds a container the way a <b>real host</b> composes logging — the logger factory
    /// registered by factory delegate / implementation type rather than as a pre-built instance —
    /// and materializes <see cref="IDataPipeline"/>, which is what a host does with the kernel.
    /// The §2.4 notice is emitted from the <c>IDataPipeline</c> singleton factory (deferred
    /// resolution), so this is the composition shape the notice must be observable under.
    /// </summary>
    private ServiceProvider BuildKernelWithHostStyleLogging(
        bool registerDefaultNormalizers,
        CapturingLoggerFactory factory)
    {
        var services = new ServiceCollection();

        // Not services.AddSingleton<ILoggerFactory>(factory): a production host never registers a
        // pre-built factory INSTANCE — AddLogging(...) registers it by implementation type. This
        // delegate shape is invisible to any composition-time descriptor scan, which is exactly the
        // defect this test pins.
        services.AddSingleton<ILoggerFactory>(_ => factory);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_root, "snapshots"),
            rawDocumentDirectory: Path.Combine(_root, "raw"),
            checkpointDirectory: Path.Combine(_root, "checkpoints"),
            eventLogDirectory: Path.Combine(_root, "events"),
            timeSeriesDirectory: Path.Combine(_root, "ts"),
            configurePipeline: o => o.RegisterDefaultNormalizers = registerDefaultNormalizers);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// AC-1.8 (primary, real-host shape) — a host that registers its logger factory the way
    /// <c>AddLogging(...)</c> does observes the flag-on <c>Information</c> registration event
    /// naming the record-type count (§2.4).
    /// </summary>
    [Fact]
    public void FlagOn_UnderHostStyleLogging_LogsInformationRegistrationEvent()
    {
        var factory = new CapturingLoggerFactory();
        using var provider = BuildKernelWithHostStyleLogging(
            registerDefaultNormalizers: true, factory);

        _ = provider.GetRequiredService<IDataPipeline>();

        factory.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Information && e.Message.Contains("9 default normalizer"));
    }

    /// <summary>
    /// AC-1.8 (primary, real-host shape) — the same host shape observes the flag-off
    /// <c>Debug</c> pass-through notice (§2.4). The default path is not silent for real hosts.
    /// </summary>
    [Fact]
    public void FlagOff_UnderHostStyleLogging_LogsDebugPassThroughNotice()
    {
        var factory = new CapturingLoggerFactory();
        using var provider = BuildKernelWithHostStyleLogging(
            registerDefaultNormalizers: false, factory);

        _ = provider.GetRequiredService<IDataPipeline>();

        factory.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Debug
            && e.Message.Contains("RegisterDefaultNormalizers = false")
            && e.Message.Contains("un-normalized"));
    }

    /// <summary>
    /// AC-1.8 — the notice is emitted exactly once per composed container: repeatedly resolving
    /// <see cref="IDataPipeline"/> does not re-emit it (the emission rides a singleton factory).
    /// </summary>
    [Fact]
    public void RepeatedPipelineResolution_DoesNotReEmitTheCompositionNotice()
    {
        var factory = new CapturingLoggerFactory();
        using var provider = BuildKernelWithHostStyleLogging(
            registerDefaultNormalizers: true, factory);

        _ = provider.GetRequiredService<IDataPipeline>();
        _ = provider.GetRequiredService<IDataPipeline>();
        _ = provider.GetRequiredService<IDataPipeline>();

        factory.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information);
    }

    // ------------------------------------------- notice reports the EFFECTIVE composed state

    /// <summary>
    /// Composes logging the way a production host does and returns the collection, so a test can
    /// drive an arbitrary number of <c>AddMarketDataKernel</c> calls or pre-register its own
    /// normalizers before building.
    /// </summary>
    private ServiceCollection NewLoggingServices(CapturingLoggerProvider sink)
    {
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(sink);
        });

        return services;
    }

    private void AddKernel(ServiceCollection services, bool registerDefaultNormalizers)
        => services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_root, "snapshots"),
            rawDocumentDirectory: Path.Combine(_root, "raw"),
            checkpointDirectory: Path.Combine(_root, "checkpoints"),
            eventLogDirectory: Path.Combine(_root, "events"),
            timeSeriesDirectory: Path.Combine(_root, "ts"),
            configurePipeline: o => o.RegisterDefaultNormalizers = registerDefaultNormalizers);

    /// <summary>
    /// The notice must report the <b>effective post-composition</b> state, not the flag captured
    /// by whichever kernel call happened to register the pipeline factory first. Under a repeated
    /// composition (first call off, second call on) the nine normalizers ARE live, so the notice
    /// must emit the registration event — emitting the pass-through line here states the exact
    /// opposite of the truth and steers an operator debugging rescaled values away from
    /// normalization as the cause.
    /// </summary>
    [Fact]
    public void RepeatedComposition_OffThenOn_NoticeReportsNormalizersRegistered()
    {
        var sink = new CapturingLoggerProvider();
        var services = NewLoggingServices(sink);
        AddKernel(services, registerDefaultNormalizers: false);
        AddKernel(services, registerDefaultNormalizers: true);

        using var provider = services.BuildServiceProvider();

        // Non-vacuous premise: normalization really is live in this composition.
        provider.GetService<INormalizer<Bar, Bar>>().Should().BeOfType<BarNormalizer>();

        _ = provider.GetRequiredService<IDataPipeline>();

        sink.Entries.Should().Contain(
            e => e.Category == s_kernelLogCategory
                && e.Level == LogLevel.Information
                && e.Message.Contains("9 default normalizer"),
            "nine kernel normalizers are registered, so the notice must say so");
        sink.Entries.Should().NotContain(
            e => e.Category == s_kernelLogCategory
                && e.Level == LogLevel.Debug
                && e.Message.Contains("un-normalized"),
            "the pass-through notice would assert the negation of the composed state");
    }

    /// <summary>
    /// The count and the record-type list name only the <b>kernel's own</b> defaults. A host that
    /// registers its own <c>INormalizer&lt;T,T&gt;</c> for a record type the kernel ships no
    /// normalizer for — <c>InstrumentMetadata</c> is absent by design — must not be reported as a
    /// tenth "default normalizer", which contradicts the shipped asymmetry in the notice's own
    /// output.
    /// </summary>
    [Fact]
    public void HostRegisteredNormalizer_IsNotCountedAmongTheKernelDefaults()
    {
        var sink = new CapturingLoggerProvider();
        var services = NewLoggingServices(sink);
        services.AddSingleton<INormalizer<InstrumentMetadata, InstrumentMetadata>>(
            new HostInstrumentMetadataNormalizer());
        AddKernel(services, registerDefaultNormalizers: true);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IDataPipeline>();

        var notice = sink.Entries.Should().ContainSingle(
            e => e.Category == s_kernelLogCategory && e.Level == LogLevel.Information).Subject;

        notice.Message.Should().Contain(
            "9 default normalizer",
            "the kernel registered exactly its own nine; the host's normalizer is not one of them");
        notice.Message.Should().NotContain(
            "InstrumentMetadata",
            "InstrumentMetadata ships no kernel normalizer, so it can never be a kernel default");
    }

    /// <summary>
    /// A record type whose normalizer the host owns — registered before the kernel, so
    /// <c>TryAddSingleton</c> yields to it — was NOT registered by the kernel, so the notice must
    /// not claim it as a default. Eight defaults were registered here, not nine.
    /// </summary>
    [Fact]
    public void HostOwnedRecordType_IsExcludedFromTheKernelDefaultCount()
    {
        var sink = new CapturingLoggerProvider();
        var services = NewLoggingServices(sink);
        services.AddSingleton<INormalizer<Bar, Bar>>(new HostBarNormalizer());
        AddKernel(services, registerDefaultNormalizers: true);

        using var provider = services.BuildServiceProvider();

        // Non-vacuous premise: the host really did win the Bar registration.
        provider.GetService<INormalizer<Bar, Bar>>().Should().BeOfType<HostBarNormalizer>();

        _ = provider.GetRequiredService<IDataPipeline>();

        var notice = sink.Entries.Should().ContainSingle(
            e => e.Category == s_kernelLogCategory && e.Level == LogLevel.Information).Subject;

        notice.Message.Should().Contain(
            "8 default normalizer",
            "TryAddSingleton yielded the Bar registration to the host, so the kernel registered eight");
    }

    /// <summary>
    /// The <c>RemoveAll</c> per-record opt-out is applied after the kernel call, so by the time the
    /// notice is emitted that record type's normalizer is genuinely gone. The notice reports the
    /// EFFECTIVE state — eight — rather than the nine the registration block itself added. This is
    /// deliberate and is the whole point of reading the composed collection back: an operator
    /// debugging why one record type is not being normalized must be told what is actually live,
    /// not what a block ran at some earlier point in composition.
    /// </summary>
    [Fact]
    public void RemoveAllOptOut_NoticeReportsTheEffectiveCountNotTheBlockCount()
    {
        var sink = new CapturingLoggerProvider();
        var services = NewLoggingServices(sink);
        AddKernel(services, registerDefaultNormalizers: true);
        services.RemoveAll<INormalizer<Bar, Bar>>();

        using var provider = services.BuildServiceProvider();

        // Non-vacuous premise: the opt-out really did take effect.
        provider.GetService<INormalizer<Bar, Bar>>().Should().BeNull();

        _ = provider.GetRequiredService<IDataPipeline>();

        var notice = sink.Entries.Should().ContainSingle(
            e => e.Category == s_kernelLogCategory && e.Level == LogLevel.Information).Subject;

        notice.Message.Should().Contain("8 default normalizer");
        notice.Message.Should().NotContain("Bar");
    }

    /// <summary>
    /// Regression guard (green before and after the disagreement fix): when the flag really IS
    /// false on the resolved options instance, the notice keeps its §2.4 wording and its Debug
    /// level — the supported default path must not start warning.
    /// </summary>
    [Fact]
    public void FlagGenuinelyOff_NoticeKeepsTheSpecWordingAtDebug()
    {
        var sink = new CapturingLoggerProvider();
        var services = NewLoggingServices(sink);
        AddKernel(services, registerDefaultNormalizers: false);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<DataPipelineOptions>>()
            .Value.RegisterDefaultNormalizers.Should().BeFalse();

        _ = provider.GetRequiredService<IDataPipeline>();

        var notice = sink.Entries.Should().ContainSingle(
            e => e.Category == s_kernelLogCategory).Subject;

        notice.Level.Should().Be(LogLevel.Debug);
        notice.Message.Should().Contain("RegisterDefaultNormalizers = false");
        notice.Message.Should().Contain("pass through un-normalized");
    }

    // ------------------------------- §2.2 AMENDED 2026-07-23 — resolution-time factories

    /// <summary>
    /// The options channel a host uses to supply <c>RegisterDefaultNormalizers</c>, and its order
    /// relative to <c>AddMarketDataKernel</c>. Every one of these reaches
    /// <see cref="IOptions{TOptions}"/>; under the amended §2.2 every one must therefore also reach
    /// the registration.
    /// </summary>
    public enum OptionsChannel
    {
        /// <summary>A <c>services.Configure&lt;DataPipelineOptions&gt;</c> after the kernel call.</summary>
        ConfigureAfterKernel,

        /// <summary>A <c>services.PostConfigure&lt;DataPipelineOptions&gt;</c> before the kernel call.</summary>
        PostConfigureBeforeKernel,

        /// <summary>A <c>services.PostConfigure&lt;DataPipelineOptions&gt;</c> after the kernel call.</summary>
        PostConfigureAfterKernel,

        /// <summary>A <c>services.ConfigureOptions&lt;T&gt;()</c> before the kernel call.</summary>
        ConfigureOptionsBeforeKernel,

        /// <summary>A <c>services.ConfigureOptions&lt;T&gt;()</c> after the kernel call.</summary>
        ConfigureOptionsAfterKernel,
    }

    /// <summary>
    /// §2.2 as amended 2026-07-23 (operator-approved, code-arena run 2026-07-21-2010-7f9323
    /// Decision #4): the kernel registers each default normalizer through a <b>resolution-time
    /// factory</b> that reads <see cref="IOptions{TOptions}"/>, not through an eager block gated on
    /// a flag value materialized before <c>AddMarketDataKernel</c> returns. The observable
    /// consequence pinned here is that a flag supplied through <b>any</b> options channel, in
    /// <b>either</b> order relative to the kernel call, actually registers the nine normalizers —
    /// the eager block could only ever see values already present when it ran, so a host binding
    /// <c>DataPipelineOptions</c> the way the class's own documentation advertises got
    /// <c>IOptions.RegisterDefaultNormalizers = true</c> and zero normalizers.
    /// </summary>
    [Theory]
    [InlineData(OptionsChannel.ConfigureAfterKernel)]
    [InlineData(OptionsChannel.PostConfigureBeforeKernel)]
    [InlineData(OptionsChannel.PostConfigureAfterKernel)]
    [InlineData(OptionsChannel.ConfigureOptionsBeforeKernel)]
    [InlineData(OptionsChannel.ConfigureOptionsAfterKernel)]
    public void FlagSuppliedThroughAnyOptionsChannel_RegistersTheKernelDefaults(
        OptionsChannel channel)
    {
        var sink = new CapturingLoggerProvider();
        var services = NewLoggingServices(sink);

        switch (channel)
        {
            case OptionsChannel.ConfigureAfterKernel:
                AddKernelWithoutPipelineOptions(services);
                services.Configure<DataPipelineOptions>(o => o.RegisterDefaultNormalizers = true);
                break;
            case OptionsChannel.PostConfigureBeforeKernel:
                services.PostConfigure<DataPipelineOptions>(o => o.RegisterDefaultNormalizers = true);
                AddKernelWithoutPipelineOptions(services);
                break;
            case OptionsChannel.PostConfigureAfterKernel:
                AddKernelWithoutPipelineOptions(services);
                services.PostConfigure<DataPipelineOptions>(o => o.RegisterDefaultNormalizers = true);
                break;
            case OptionsChannel.ConfigureOptionsBeforeKernel:
                services.ConfigureOptions<FlagOnPipelineOptionsConfigurer>();
                AddKernelWithoutPipelineOptions(services);
                break;
            default:
                AddKernelWithoutPipelineOptions(services);
                services.ConfigureOptions<FlagOnPipelineOptionsConfigurer>();
                break;
        }

        using var provider = services.BuildServiceProvider();

        // Non-vacuous premise: the channel really did bind on the surface DataPipeline reads.
        provider.GetRequiredService<IOptions<DataPipelineOptions>>()
            .Value.RegisterDefaultNormalizers.Should().BeTrue();

        provider.GetService<INormalizer<Bar, Bar>>().Should().BeOfType<BarNormalizer>(
            "the amended §2.2 registration reads the flag at RESOLUTION time, so a value supplied "
            + "through any options channel in any order reaches the registration; the eager block "
            + "could only see values present at the moment it ran");

        _ = provider.GetRequiredService<IDataPipeline>();

        var notice = sink.Entries.Should().ContainSingle(
            e => e.Category == s_kernelLogCategory).Subject;

        notice.Level.Should().Be(
            LogLevel.Information,
            "the nine defaults are live in this composition, so the notice must report them");
        notice.Message.Should().Contain("9 default normalizer");
    }

    /// <summary>
    /// NEW-C1R2-01 — the disagreement <c>Warning</c> introduced by <c>a60a2f2</c> fired on a
    /// <b>correct</b> composition: a host that supplies its own <c>INormalizer&lt;T,T&gt;</c> for
    /// all nine record types with the flag on registered zero <i>kernel</i> defaults, tripping the
    /// branch, which then asserted "records pass through un-normalized" while the host's
    /// normalizers resolved for every record type. The rewritten derivation resolves off the built
    /// provider, so it can tell "nothing resolves" from "the host's own resolves" and the false
    /// alarm is structurally unreachable, not merely re-worded.
    /// </summary>
    [Fact]
    public void HostOwnsEveryRecordType_NoticeDoesNotClaimRecordsPassThroughUnNormalized()
    {
        var sink = new CapturingLoggerProvider();
        var services = NewLoggingServices(sink);
        RegisterHostNormalizersForEveryRecordType(services);
        AddKernel(services, registerDefaultNormalizers: true);

        using var provider = services.BuildServiceProvider();

        // Non-vacuous premises: the flag really is on, and normalization really is live — the
        // host's own implementation resolves for every record type the kernel ships one for.
        provider.GetRequiredService<IOptions<DataPipelineOptions>>()
            .Value.RegisterDefaultNormalizers.Should().BeTrue();
        provider.GetService<INormalizer<Bar, Bar>>().Should().BeOfType<HostNormalizer<Bar>>();
        provider.GetService<INormalizer<YieldCurveQuote, YieldCurveQuote>>()
            .Should().BeOfType<HostNormalizer<YieldCurveQuote>>();

        _ = provider.GetRequiredService<IDataPipeline>();

        var notice = sink.Entries.Should().ContainSingle(
            e => e.Category == s_kernelLogCategory).Subject;

        notice.Message.Should().NotContain(
            "records pass through un-normalized",
            "the host's normalizer resolves for every record type, so this composition normalizes "
            + "everything — asserting the opposite is the NEW-C1R2-01 false alarm");
        notice.Level.Should().NotBe(
            LogLevel.Warning,
            "a composition the spec documents as correct (§2.3 host-override-wins) must not warn");
        notice.Message.Should().Contain(
            "host registers its own",
            "the notice must name what actually happened: the kernel registered no defaults "
            + "because the host owns every record type");
    }

    /// <summary>
    /// R2-3 / <c>DI-OPTIONS-STALEFLAG-020</c> — repeated composition on→off. Under the eager block
    /// the first call's registrations were permanent (<c>TryAdd</c> is monotonic) so nine
    /// normalizers stayed live while <c>IOptions</c> reported <see langword="false"/>: the two
    /// surfaces disagreed. Under the amended §2.2 the factory reads the flag at resolution, so the
    /// last-wins options value governs both surfaces and they agree — the flag is no longer stale.
    /// </summary>
    [Fact]
    public void RepeatedComposition_OnThenOff_LeavesBothSurfacesAgreeingOnOff()
    {
        var sink = new CapturingLoggerProvider();
        var services = NewLoggingServices(sink);
        AddKernel(services, registerDefaultNormalizers: true);
        AddKernel(services, registerDefaultNormalizers: false);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<DataPipelineOptions>>()
            .Value.RegisterDefaultNormalizers.Should().BeFalse(
                "IConfigureOptions accumulate and the last registrant wins per property");

        provider.GetService<INormalizer<Bar, Bar>>().Should().BeNull(
            "the composition surface must observe the same last-wins value as IOptions — under "
            + "the eager block the first call's registration survived the second call's off");

        _ = provider.GetRequiredService<IDataPipeline>();

        var notice = sink.Entries.Should().ContainSingle(
            e => e.Category == s_kernelLogCategory).Subject;

        notice.Level.Should().Be(LogLevel.Debug);
        notice.Message.Should().Contain("RegisterDefaultNormalizers = false");
    }

    /// <summary>
    /// The disagreement <c>Warning</c> survives the amendment, but only for the one shape that can
    /// still reach it: the flag on with <c>RemoveAll</c> applied to every record type. Its
    /// remediation text must be re-derived — blaming a composition-time flag read is advice for a
    /// mechanism the amended §2.2 removed, and it misdirects the only operator who can now see the
    /// message (one who did pass the flag correctly and then opted every record type out).
    /// </summary>
    [Fact]
    public void RemoveAllOfEveryRecordType_FlagOn_NoticeWarnsWithoutBlamingCompositionTimeReads()
    {
        var sink = new CapturingLoggerProvider();
        var services = NewLoggingServices(sink);
        AddKernel(services, registerDefaultNormalizers: true);
        RemoveAllNormalizersForEveryRecordType(services);

        using var provider = services.BuildServiceProvider();

        // Non-vacuous premises: the flag is on and genuinely nothing resolves.
        provider.GetRequiredService<IOptions<DataPipelineOptions>>()
            .Value.RegisterDefaultNormalizers.Should().BeTrue();
        provider.GetService<INormalizer<Bar, Bar>>().Should().BeNull();

        _ = provider.GetRequiredService<IDataPipeline>();

        var notice = sink.Entries.Should().ContainSingle(
            e => e.Category == s_kernelLogCategory).Subject;

        notice.Level.Should().Be(LogLevel.Warning);
        notice.Message.Should().Contain("RegisterDefaultNormalizers = true");
        notice.Message.Should().NotContain("RegisterDefaultNormalizers = false");
        notice.Message.Should().Contain(
            "RemoveAll",
            "the opt-out is the only channel that can still produce this state");
        notice.Message.Should().NotContain(
            "composition time",
            "the amended §2.2 reads the flag at resolution time, so blaming a composition-time "
            + "read is advice for a mechanism that no longer exists");
    }

    /// <summary>
    /// Host-supplied normalizer double for an arbitrary record type, standing in for a downstream
    /// repo that owns normalization for every record type itself.
    /// </summary>
    private sealed class HostNormalizer<TRecord> : INormalizer<TRecord, TRecord>
    {
        public TRecord Normalize(TRecord raw) => raw;
    }

    /// <summary>
    /// An <c>IConfigureOptions&lt;DataPipelineOptions&gt;</c> registered by TYPE, the shape
    /// <c>services.ConfigureOptions&lt;T&gt;()</c> uses. It needs an <c>IServiceProvider</c> to
    /// construct, so no composition-time descriptor read can ever apply it.
    /// </summary>
    private sealed class FlagOnPipelineOptionsConfigurer : IConfigureOptions<DataPipelineOptions>
    {
        public void Configure(DataPipelineOptions options)
            => options.RegisterDefaultNormalizers = true;
    }

    /// <summary>
    /// Registers a host-owned normalizer for each of the nine record types the kernel ships
    /// normalizers for.
    /// </summary>
    private static void RegisterHostNormalizersForEveryRecordType(IServiceCollection services)
    {
        services.AddSingleton<INormalizer<Bar, Bar>>(new HostNormalizer<Bar>());
        services.AddSingleton<INormalizer<CorporateAction, CorporateAction>>(new HostNormalizer<CorporateAction>());
        services.AddSingleton<INormalizer<DividendPayment, DividendPayment>>(new HostNormalizer<DividendPayment>());
        services.AddSingleton<INormalizer<FactorObservation, FactorObservation>>(new HostNormalizer<FactorObservation>());
        services.AddSingleton<INormalizer<FuturesSettlement, FuturesSettlement>>(new HostNormalizer<FuturesSettlement>());
        services.AddSingleton<INormalizer<FxRate, FxRate>>(new HostNormalizer<FxRate>());
        services.AddSingleton<INormalizer<OptionQuote, OptionQuote>>(new HostNormalizer<OptionQuote>());
        services.AddSingleton<INormalizer<ScalarObservation, ScalarObservation>>(new HostNormalizer<ScalarObservation>());
        services.AddSingleton<INormalizer<YieldCurveQuote, YieldCurveQuote>>(new HostNormalizer<YieldCurveQuote>());
    }

    /// <summary>
    /// Applies the §2.3 per-record opt-out to every record type — the one composition that can
    /// still reach the disagreement <c>Warning</c> under the amended §2.2.
    /// </summary>
    private static void RemoveAllNormalizersForEveryRecordType(IServiceCollection services)
    {
        services.RemoveAll<INormalizer<Bar, Bar>>();
        services.RemoveAll<INormalizer<CorporateAction, CorporateAction>>();
        services.RemoveAll<INormalizer<DividendPayment, DividendPayment>>();
        services.RemoveAll<INormalizer<FactorObservation, FactorObservation>>();
        services.RemoveAll<INormalizer<FuturesSettlement, FuturesSettlement>>();
        services.RemoveAll<INormalizer<FxRate, FxRate>>();
        services.RemoveAll<INormalizer<OptionQuote, OptionQuote>>();
        services.RemoveAll<INormalizer<ScalarObservation, ScalarObservation>>();
        services.RemoveAll<INormalizer<YieldCurveQuote, YieldCurveQuote>>();
    }

    /// <summary>
    /// Composes the kernel WITHOUT supplying a <c>configurePipeline</c> delegate, so the only
    /// values on the options surface are the ones the host itself registers. This is the shape a
    /// host uses when it binds <c>DataPipelineOptions</c> through the standard options pattern.
    /// </summary>
    private void AddKernelWithoutPipelineOptions(ServiceCollection services)
        => services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_root, "snapshots"),
            rawDocumentDirectory: Path.Combine(_root, "raw"),
            checkpointDirectory: Path.Combine(_root, "checkpoints"),
            eventLogDirectory: Path.Combine(_root, "events"),
            timeSeriesDirectory: Path.Combine(_root, "ts"));

    /// <summary>
    /// Host-supplied normalizer double for a record type the kernel ships none for.
    /// </summary>
    private sealed class HostInstrumentMetadataNormalizer
        : INormalizer<InstrumentMetadata, InstrumentMetadata>
    {
        public InstrumentMetadata Normalize(InstrumentMetadata raw) => raw;
    }

    /// <summary>
    /// The category the kernel logs the §2.4 notice under. Assertions filter on it so an unrelated
    /// entry from another category cannot satisfy — or vacuously break — a §2.4 assertion.
    /// </summary>
    private static readonly string s_kernelLogCategory =
        typeof(ServiceCollectionExtensions).FullName!;

    /// <summary>
    /// Capturing <see cref="ILoggerProvider"/> registered through the logging builder, so a test
    /// can observe what a production host's logging pipeline would see.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(string Category, LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName)
            => new CategoryCapturingLogger(categoryName, Entries);

        public void Dispose()
        {
            // Nothing to release.
        }

        private sealed class CategoryCapturingLogger(
            string category,
            List<(string Category, LogLevel Level, string Message)> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (sink)
                {
                    sink.Add((category, logLevel, formatter(state, exception)));
                }
            }
        }
    }

    /// <summary>
    /// Minimal <see cref="ILoggerFactory"/> double capturing every formatted entry, used by the
    /// non-builder container shapes (instance-registered and factory-delegate).
    /// </summary>
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void AddProvider(ILoggerProvider provider)
        {
            // No provider chaining: this double is the sink.
        }

        public void Dispose()
        {
            // Nothing to release.
        }

        private sealed class CapturingLogger(List<(LogLevel Level, string Message)> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => sink.Add((logLevel, formatter(state, exception)));
        }
    }
}
