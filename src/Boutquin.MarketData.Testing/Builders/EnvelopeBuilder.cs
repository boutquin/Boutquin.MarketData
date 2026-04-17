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
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Results;

namespace Boutquin.MarketData.Testing.Builders;

/// <summary>
/// Fluent builder for constructing <see cref="DataEnvelope{TPayload}"/> instances in tests.
/// Eliminates boilerplate when setting up adapter responses or pipeline results.
/// </summary>
/// <typeparam name="TPayload">The payload type of the envelope.</typeparam>
/// <remarks>
/// Defaults produce a complete, issue-free envelope with synthetic provenance.
/// Override individual fields as needed for specific test scenarios (e.g., partial coverage,
/// stale data warnings, error issues).
/// </remarks>
public sealed class EnvelopeBuilder<TPayload>
{
    private TPayload? _payload;
    private DataCoverage _coverage = new(0, 0, 0, 1.0m);
    private readonly List<DataIssue> _issues = new();
    private readonly List<DataProvenance> _provenance = new();

    /// <summary>
    /// Attaches the result payload carried by the envelope under construction.
    /// </summary>
    /// <param name="payload">The data payload the envelope will carry. Must be set before <see cref="Build"/> or the built envelope will contain a <see langword="null"/> payload.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EnvelopeBuilder<TPayload> WithPayload(TPayload payload)
    {
        _payload = payload;
        return this;
    }

    /// <summary>
    /// Configures explicit coverage metrics on the envelope. Use this to simulate partial
    /// fetches, empty responses, or specific gap-analysis scenarios that the default
    /// complete-coverage value would hide.
    /// </summary>
    /// <param name="requested">Expected point count based on the request's date range and frequency.</param>
    /// <param name="returned">Actual point count the fixture represents returning to the caller.</param>
    /// <param name="missing">Expected points that were not returned; typically <c>requested - returned</c> but kept separate to simulate sources that report "known-gap" days distinctly from silent gaps.</param>
    /// <param name="ratio">Returned/requested ratio in the range [0.0, 1.0]. Supplied explicitly rather than derived to let tests exercise rounding or downgraded-completeness paths independently of the count triple.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EnvelopeBuilder<TPayload> WithCoverage(int requested, int returned, int missing, decimal ratio)
    {
        _coverage = new DataCoverage(requested, returned, missing, ratio);
        return this;
    }

    /// <summary>
    /// Appends a data-quality issue to the envelope. Use to simulate upstream warnings or
    /// errors that a fallback evaluator or pipeline step should react to.
    /// </summary>
    /// <param name="code">Stable machine-readable issue code. Pass one of the <see cref="IssueCode"/> well-known statics or construct a new value for an adapter-specific failure mode.</param>
    /// <param name="severity">Severity classification — <see cref="IssueSeverity.Warning"/> for recoverable gaps, <see cref="IssueSeverity.Error"/> for data the consumer should not trust.</param>
    /// <param name="message">Human-readable description surfaced in logs and diagnostics.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EnvelopeBuilder<TPayload> WithIssue(IssueCode code, IssueSeverity severity, string message)
    {
        _issues.Add(new DataIssue(code, severity, message));
        return this;
    }

    /// <summary>
    /// Appends a provenance record representing one adapter contribution to the envelope.
    /// </summary>
    /// <param name="provider">Typed provider key (e.g. <c>new ProviderCode("tiingo")</c>).</param>
    /// <param name="dataset">Dataset identifier scoped to the provider (for example the symbol or series ID).</param>
    /// <param name="licenseFlag">License classification — defaults to <see cref="LicenseType.Free"/>.</param>
    /// <param name="retrievalMode">How the record was obtained — defaults to <see cref="RetrievalMode.Api"/>.</param>
    /// <param name="freshness">Classification of how current the upstream data was at retrieval time.</param>
    /// <param name="sourceUrl">URL that produced the record when known, or <see langword="null"/> for cache/snapshot replays and synthetic fixtures.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public EnvelopeBuilder<TPayload> WithProvenance(
        ProviderCode provider, string dataset,
        LicenseType licenseFlag = LicenseType.Free,
        RetrievalMode retrievalMode = RetrievalMode.Api,
        FreshnessClass freshness = FreshnessClass.Unknown,
        string? sourceUrl = null)
    {
        _provenance.Add(new DataProvenance(provider, dataset, licenseFlag, retrievalMode, freshness, DateTimeOffset.UtcNow, sourceUrl));
        return this;
    }

    /// <summary>
    /// Builds the envelope. If no provenance was added, a default "test-provider" entry is included.
    /// </summary>
    /// <returns>The constructed <see cref="DataEnvelope{TPayload}"/>.</returns>
    public DataEnvelope<TPayload> Build()
    {
        if (_provenance.Count == 0)
        {
            _provenance.Add(new DataProvenance(
                new ProviderCode("test-provider"),
                "test-dataset",
                LicenseType.Free,
                RetrievalMode.Fixture,
                FreshnessClass.Unknown,
                DateTimeOffset.UtcNow,
                null));
        }

        return new DataEnvelope<TPayload>(
            _payload!,
            _coverage,
            _issues.AsReadOnly(),
            _provenance.AsReadOnly());
    }
}
