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
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;

namespace Boutquin.MarketData.Testing.Fakes;

/// <summary>
/// Deterministic fake option-chain adapter. Emits a fixed set of <see cref="OptionChainRow"/>s —
/// one per listed contract — all
/// co-dated on the requested range's start date, so the keyed-row envelope can be exercised
/// end-to-end without a live chain source.
/// </summary>
/// <remarks>
/// This is the <c>TFetched = OptionChainRow</c> half of the fetched-differs-from-stored fan-out: the
/// adapter returns contract-identity-bearing rows, and the pipeline's per-row fan-out projects each to
/// its own <see cref="Boutquin.MarketData.Abstractions.Keys.OptionQuoteDatasetKey"/> and stores the
/// slim <see cref="OptionQuote"/> body. The <see cref="FetchCount"/> counter lets a covered re-read be
/// shown NOT to re-dispatch.
/// </remarks>
public sealed class FakeOptionChainAdapter
    : IDataSourceAdapter<OptionChainRequest, OptionChainRow>, IPrioritizedAdapter
{
    private readonly IReadOnlyList<OptionChainRow> _rows;

    /// <summary>
    /// Creates the fake adapter over a fixed contract set.
    /// </summary>
    /// <param name="rows">
    /// The chain rows to emit. Each row's <see cref="OptionChainRow.Contract"/> is the per-row
    /// discriminant; each <see cref="OptionChainRow.Quote"/>'s observation fields are echoed back
    /// verbatim, re-dated on <see cref="FetchAsync"/> to the requested range's start date so every
    /// contract is co-dated.
    /// </param>
    /// <param name="provider">The provider key stamped on the emitted envelope. Defaults to <c>"opra"</c>.</param>
    public FakeOptionChainAdapter(IReadOnlyList<OptionChainRow> rows, ProviderCode? provider = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows;
        ProviderKey = provider ?? new ProviderCode("opra");
    }

    /// <summary>The number of times <see cref="FetchAsync"/> has been invoked.</summary>
    public int FetchCount { get; private set; }

    /// <inheritdoc />
    public ProviderCode ProviderKey { get; }

    /// <summary>Priority for the fallback orchestrator; a single high-priority fake.</summary>
    public int Priority => 100;

    /// <inheritdoc />
    public bool CanHandle(OptionChainRequest request) => true;

    /// <inheritdoc />
    public Task<DataEnvelope<IReadOnlyList<OptionChainRow>>> FetchAsync(
        OptionChainRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        FetchCount++;

        var asOf = request.Range.From;
        var rows = _rows
            .Select(r => r with { Quote = r.Quote with { Date = asOf } })
            .ToList();

        var coverage = new DataCoverage(rows.Count, rows.Count, 0, rows.Count > 0 ? 1.0m : 0.0m);
        var provenance = new DataProvenance(
            ProviderKey, request.DatasetKey, LicenseType.Free, RetrievalMode.Api,
            FreshnessClass.EndOfDay, DateTimeOffset.UtcNow,
            $"https://{ProviderKey.Value}.test/{request.DatasetKey}");

        return Task.FromResult(new DataEnvelope<IReadOnlyList<OptionChainRow>>(
            rows, coverage, [], [provenance]));
    }
}
