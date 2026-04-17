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
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Results;
using Microsoft.Extensions.Logging;

namespace Boutquin.MarketData.Orchestration.Fallback;

/// <summary>
/// Tries data source adapters in priority order until one produces a response accepted
/// by the evaluator. Records an audit trail of every attempt for provenance tracking.
/// </summary>
/// <typeparam name="TRequest">The request type consumed by the adapters.</typeparam>
/// <typeparam name="TRecord">The record type produced by the adapters.</typeparam>
/// <remarks>
/// <para>
/// Adapters implementing <see cref="IPrioritizedAdapter"/> are sorted by descending priority.
/// Unranked adapters (priority 0) are tried last. Within the same priority, adapters are
/// tried in registration order.
/// </para>
/// <para>
/// When an adapter throws, the exception is caught and recorded as a failed attempt.
/// Orchestration continues to the next adapter. If no adapter succeeds, an
/// <see cref="InvalidOperationException"/> is thrown containing all attempt details.
/// </para>
/// </remarks>
public sealed class FallbackOrchestrator<TRequest, TRecord>
    where TRequest : IDataRequest
{
    private readonly ILogger<FallbackOrchestrator<TRequest, TRecord>> _logger;

    /// <summary>
    /// Initializes a new <see cref="FallbackOrchestrator{TRequest, TRecord}"/>.
    /// </summary>
    /// <param name="logger">Logger for structured diagnostics.</param>
    public FallbackOrchestrator(ILogger<FallbackOrchestrator<TRequest, TRecord>> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Runs the adapter fallback chain and returns the first response the evaluator accepts,
    /// together with the audit trail of every adapter attempted.
    /// </summary>
    /// <param name="request">The data request to fulfill.</param>
    /// <param name="adapters">The adapters to try; sorted internally by descending priority then by provider key.</param>
    /// <param name="evaluator">Decides whether an adapter response is usable (coverage, freshness, domain checks).</param>
    /// <param name="cancellationToken">Cooperative cancellation. A cancelled operation propagates <see cref="OperationCanceledException"/> without wrapping.</param>
    /// <returns>A <see cref="FallbackResult{TResponse}"/> carrying the accepted response, the winning provider key, and the full attempt audit trail.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the adapter list is empty or no adapter produced an accepted response; the message includes every attempt outcome for provenance.</exception>
    /// <remarks>
    /// <para>
    /// Ordering: adapters are sorted by descending <see cref="IPrioritizedAdapter.Priority"/>, then by
    /// <see cref="IDataSourceAdapter{TRequest,TRecord}.ProviderKey"/> using ordinal comparison on the
    /// already-lower-cased <see cref="ProviderCode.Value"/> so ties are deterministic across platforms.
    /// Non-prioritised adapters are treated as priority 0.
    /// </para>
    /// <para>
    /// Failure modes: an adapter that throws is recorded as a failed attempt and the chain continues;
    /// a response the evaluator rejects is recorded with the evaluator's reason and the chain continues.
    /// Only <see cref="OperationCanceledException"/> triggered by the supplied token escapes without
    /// being logged as a failed attempt — every other exception is captured so the caller sees a complete
    /// provenance record when <see cref="InvalidOperationException"/> is raised.
    /// </para>
    /// <para>
    /// Common mistake: configuring the orchestrator with a single strict evaluator and one adapter
    /// turns the fallback chain into a pass-through that throws on any rejection. Pair the orchestrator
    /// with at least one permissive adapter (for example a stale-cache or default-value fallback) when
    /// the consumer tolerates degraded coverage.
    /// </para>
    /// </remarks>
    public async Task<FallbackResult<DataEnvelope<IReadOnlyList<TRecord>>>> ExecuteAsync(
        TRequest request,
        IReadOnlyList<IDataSourceAdapter<TRequest, TRecord>> adapters,
        IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<TRecord>>> evaluator,
        CancellationToken cancellationToken = default)
    {
        var ordered = OrderByPriority(adapters);
        var attempts = new List<FallbackAttempt>(ordered.Count);

        foreach (var adapter in ordered)
        {
            var priority = GetPriority(adapter);
            var providerKey = adapter.ProviderKey;

            try
            {
                _logger.LogDebug(
                    "Attempting adapter {ProviderKey} (priority {Priority}) for {DatasetKey}",
                    providerKey, priority, request.DatasetKey);

                var response = await adapter
                    .FetchAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                if (evaluator.IsUsable(response, out var reason))
                {
                    _logger.LogInformation(
                        "Adapter {ProviderKey} succeeded for {DatasetKey} (coverage {CoverageRatio:P1})",
                        providerKey, request.DatasetKey, response.Coverage.CoverageRatio);

                    attempts.Add(new FallbackAttempt(providerKey, priority, Succeeded: true, "Accepted"));
                    return new FallbackResult<DataEnvelope<IReadOnlyList<TRecord>>>(
                        response, providerKey, attempts);
                }

                var rejectOutcome = $"Rejected — {reason ?? "evaluator returned false"}";
                _logger.LogWarning(
                    "Adapter {ProviderKey} response rejected for {DatasetKey}: {Reason}",
                    providerKey, request.DatasetKey, rejectOutcome);

                attempts.Add(new FallbackAttempt(providerKey, priority, Succeeded: false, rejectOutcome));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var errorOutcome = $"Exception: {ex.GetType().Name}: {ex.Message}";
                _logger.LogWarning(
                    ex,
                    "Adapter {ProviderKey} failed for {DatasetKey}: {Error}",
                    providerKey, request.DatasetKey, errorOutcome);

                attempts.Add(new FallbackAttempt(providerKey, priority, Succeeded: false, errorOutcome));
            }
        }

        throw new InvalidOperationException(
            $"All {attempts.Count} adapter(s) failed for dataset '{request.DatasetKey}'. " +
            $"Attempts: [{string.Join("; ", attempts.Select(a => $"{a.ProviderKey}: {a.Outcome}"))}].");
    }

    // Ties on priority are broken by provider key with OrdinalIgnoreCase so the fallback
    // order is deterministic regardless of DI registration order and host-locale collation
    // (StringComparer.CurrentCulture would pick different winners on tr-TR vs en-US).
    private static List<IDataSourceAdapter<TRequest, TRecord>> OrderByPriority(
        IReadOnlyList<IDataSourceAdapter<TRequest, TRecord>> adapters) =>
        adapters
            .OrderByDescending(GetPriority)
            .ThenBy(a => a.ProviderKey)
            .ToList();

    private static int GetPriority(IDataSourceAdapter<TRequest, TRecord> adapter) =>
        adapter is IPrioritizedAdapter prioritized ? prioritized.Priority : 0;
}
