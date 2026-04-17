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
using Boutquin.MarketData.Abstractions.Results;

namespace Boutquin.MarketData.Orchestration.Fallback;

/// <summary>
/// Evaluates whether a <see cref="DataEnvelope{TPayload}"/> is usable based on coverage status.
/// Accepts <see cref="CompletenessStatus.Complete"/> and <see cref="CompletenessStatus.Partial"/>
/// responses; rejects <see cref="CompletenessStatus.Missing"/>.
/// </summary>
/// <typeparam name="TRecord">The record type in the envelope payload list.</typeparam>
/// <remarks>
/// <para>
/// This is the default evaluator for the fallback orchestrator. Domain repos can provide
/// stricter evaluators (e.g., rejecting partial results or enforcing minimum coverage ratios)
/// by implementing <see cref="IFallbackResponseEvaluator{TResponse}"/> directly — the
/// <see cref="MinimumCoverageRatio"/> constructor parameter is the lightweight escape hatch for
/// "same logic, just higher bar" scenarios.
/// </para>
/// <para>
/// Evaluator totality: this class handles <see langword="null"/> responses explicitly so
/// misbehaving adapters returning <c>Task.FromResult&lt;DataEnvelope&lt;...&gt;&gt;(null!)</c>
/// surface as a fallback trigger rather than crashing the orchestrator with an NRE.
/// </para>
/// </remarks>
public sealed class CoverageResponseEvaluator<TRecord>
    : IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<TRecord>>>
{
    private readonly decimal _minimumCoverageRatio;

    /// <summary>
    /// Initializes the evaluator with the default threshold (accepts any
    /// <see cref="CompletenessStatus.Partial"/> result regardless of ratio).
    /// </summary>
    public CoverageResponseEvaluator()
        : this(0m)
    {
    }

    /// <summary>
    /// Initializes the evaluator with a minimum coverage ratio. Responses with
    /// <c>Coverage.CoverageRatio &lt; minimumCoverageRatio</c> are rejected even if their
    /// status is <see cref="CompletenessStatus.Partial"/>.
    /// </summary>
    /// <param name="minimumCoverageRatio">
    /// Coverage ratio threshold in the interval <c>[0, 1]</c>. Defaults to <c>0</c> (permissive).
    /// Strict pipelines typically set this to <c>0.95</c> or higher.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="minimumCoverageRatio"/> is outside <c>[0, 1]</c>.
    /// </exception>
    public CoverageResponseEvaluator(decimal minimumCoverageRatio)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumCoverageRatio, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumCoverageRatio, 1m);
        _minimumCoverageRatio = minimumCoverageRatio;
    }

    /// <summary>Minimum coverage ratio below which <see cref="IsUsable"/> returns <see langword="false"/>.</summary>
    public decimal MinimumCoverageRatio => _minimumCoverageRatio;

    /// <inheritdoc />
    public bool IsUsable(DataEnvelope<IReadOnlyList<TRecord>> response, out string? reason)
    {
        if (response is null)
        {
            reason = "Adapter returned a null envelope";
            return false;
        }

        if (response.Payload is null || response.Payload.Count == 0)
        {
            reason = "Payload is null or empty";
            return false;
        }

        if (response.Coverage.Status == CompletenessStatus.Missing)
        {
            reason = $"Coverage status is Missing (ratio {response.Coverage.CoverageRatio:P1})";
            return false;
        }

        if (response.Coverage.CoverageRatio < _minimumCoverageRatio)
        {
            reason = $"Coverage ratio {response.Coverage.CoverageRatio:P1} is below minimum {_minimumCoverageRatio:P1}";
            return false;
        }

        reason = null;
        return true;
    }
}
