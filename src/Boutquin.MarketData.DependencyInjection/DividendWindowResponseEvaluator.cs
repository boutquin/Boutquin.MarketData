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
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Orchestration.Fallback;

namespace Boutquin.MarketData.DependencyInjection;

/// <summary>
/// Fallback-acceptance rule for cash-dividend fetches: a window that contains no ex-dates is an
/// empty <b>success</b>, not a failed fetch.
/// </summary>
/// <remarks>
/// <para>
/// Dividends are event-sparse and many instruments never distribute at all, so "the payload is
/// empty" carries a completely different meaning here than it does for a sampled series. The
/// general-purpose <see cref="CoverageResponseEvaluator{TRecord}"/> treats an empty payload as a
/// rejection before it ever consults coverage, which is the right default for a price or rate
/// series — every trading day in range is expected to yield a point, so an empty payload means
/// the fetch did not work. Applied to dividends it produces the opposite of the truth: a
/// non-paying symbol exhausts the fallback chain and surfaces as a failed partition,
/// indistinguishable from a provider outage. A consumer deriving a trailing income yield cannot
/// then tell a genuine zero from missing data — the two demand opposite responses.
/// </para>
/// <para>
/// This evaluator changes exactly one verdict and defers everything else to the shipped default:
/// an empty payload is accepted only when the adapter also reported <b>complete</b> coverage —
/// i.e. the adapter affirmatively said it fetched the entire
/// window and found nothing in it. A null envelope, a payload rejected on coverage status, and
/// the minimum-coverage-ratio bar are all evaluated by
/// <see cref="CoverageResponseEvaluator{TRecord}"/> exactly as before, so a real provider failure
/// still triggers fallback to the next adapter.
/// </para>
/// </remarks>
internal sealed class DividendWindowResponseEvaluator
    : IFallbackResponseEvaluator<DataEnvelope<IReadOnlyList<DividendPayment>>>
{
    private readonly CoverageResponseEvaluator<DividendPayment> _default = new();

    /// <inheritdoc />
    public bool IsUsable(DataEnvelope<IReadOnlyList<DividendPayment>> response, out string? reason)
    {
        if (response is { Payload.Count: 0, Coverage: not null }
            && response.Coverage.Status == CompletenessStatus.Complete)
        {
            // The adapter reached the provider, parsed the window and found no ex-dates. That is
            // the answer, not the absence of one — accept it rather than falling through to an
            // adapter that cannot know any better.
            reason = null;
            return true;
        }

        return _default.IsUsable(response, out reason);
    }
}
