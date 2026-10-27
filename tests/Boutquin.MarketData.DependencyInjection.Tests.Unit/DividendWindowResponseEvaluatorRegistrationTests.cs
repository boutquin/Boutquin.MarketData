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

using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Results;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// Covers the dividend-specific fallback-acceptance rule the kernel registers: that it is
/// reachable through <see cref="ServiceCollectionExtensions.AddMarketDataKernel"/>, that it
/// re-decides only the empty-window case, and — the containment claim — that it leaves every
/// other record type on the shipped default.
/// </summary>
public sealed class DividendWindowResponseEvaluatorRegistrationTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"marketdata-divevaluator-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private ServiceProvider BuildKernel()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMarketDataKernel(
            snapshotDirectory: Path.Combine(_tempDir, "snapshots"),
            rawDocumentDirectory: Path.Combine(_tempDir, "raw"),
            checkpointDirectory: Path.Combine(_tempDir, "checkpoints"),
            eventLogDirectory: Path.Combine(_tempDir, "events"));
        return services.BuildServiceProvider();
    }

    private static DataProvenance Provenance() => new(
        new ProviderCode("test"), "VYM", LicenseType.Free, RetrievalMode.Api,
        FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, "https://test.invalid/vym");

    private static DataEnvelope<IReadOnlyList<DividendPayment>> Envelope(
        IReadOnlyList<DividendPayment> payload, decimal coverageRatio) =>
        new(payload, new DataCoverage(payload.Count, payload.Count, 0, coverageRatio), [], [Provenance()]);

    private static IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<DividendPayment>>> Resolve(
        ServiceProvider provider) =>
        provider.GetRequiredService<IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<DividendPayment>>>>();

    [Fact]
    public void AddMarketDataKernel_registers_a_dividend_specific_fallback_evaluator()
    {
        using var provider = BuildKernel();

        // Resolved exactly the way the pipeline resolves it before dispatching a fetch, so this
        // fails if the type exists but nothing in the production wiring reaches it.
        provider.GetService<IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<DividendPayment>>>>()
            .Should().NotBeNull();
    }

    /// <summary>
    /// The containment claim. The registration is closed over <see cref="DividendPayment"/>, so
    /// every other record type must still resolve nothing and fall through to the shipped default
    /// inside the pipeline. If this ever starts returning an evaluator, the dividend rule has
    /// leaked into record types whose empty payloads really do mean a failed fetch.
    /// </summary>
    [Fact]
    public void The_dividend_evaluator_does_not_apply_to_other_record_types()
    {
        using var provider = BuildKernel();

        provider.GetService<IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<Bar>>>>()
            .Should().BeNull("price bars keep the default empty-payload-is-a-rejection rule");
        provider.GetService<IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<ScalarObservation>>>>()
            .Should().BeNull("economic series keep the default rule");
        provider.GetService<IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<FxRate>>>>()
            .Should().BeNull("fx rates keep the default rule");
    }

    [Fact]
    public void An_empty_window_with_complete_coverage_is_usable()
    {
        using var provider = BuildKernel();

        Resolve(provider).IsUsable(Envelope([], coverageRatio: 1.0m), out var reason)
            .Should().BeTrue("the adapter fetched the window and found no ex-dates");
        reason.Should().BeNull();
    }

    [Fact]
    public void An_empty_window_with_partial_coverage_is_rejected()
    {
        using var provider = BuildKernel();

        Resolve(provider).IsUsable(Envelope([], coverageRatio: 0.5m), out var reason)
            .Should().BeFalse("an incompletely observed window must fall through to another adapter");
        reason.Should().NotBeNull();
    }

    [Fact]
    public void An_empty_window_reported_as_missing_is_still_rejected()
    {
        using var provider = BuildKernel();

        // CoverageRatio 0 classifies as Missing — the adapter is saying it could not cover the
        // window, which is a genuine failure and must still trigger fallback to the next adapter.
        Resolve(provider).IsUsable(Envelope([], coverageRatio: 0m), out var reason)
            .Should().BeFalse("an adapter that could not cover the window has not answered it");
        reason.Should().NotBeNull();
    }

    [Fact]
    public void A_null_envelope_is_still_rejected()
    {
        using var provider = BuildKernel();

        Resolve(provider).IsUsable(null!, out var reason)
            .Should().BeFalse("a misbehaving adapter must trigger fallback, not crash the chain");
        reason.Should().NotBeNull();
    }

    [Fact]
    public void A_populated_window_is_evaluated_by_the_default_rule()
    {
        using var provider = BuildKernel();
        DividendPayment[] payments =
        [
            new(new DateOnly(2024, 3, 20), 0.75m, Currency.USD, DividendType.Unclassified),
        ];

        Resolve(provider).IsUsable(Envelope(payments, coverageRatio: 1.0m), out _)
            .Should().BeTrue("a complete window carrying distributions is usable");
        Resolve(provider).IsUsable(Envelope(payments, coverageRatio: 0m), out _)
            .Should().BeFalse("Missing coverage is rejected exactly as the default evaluator does");
    }
}
