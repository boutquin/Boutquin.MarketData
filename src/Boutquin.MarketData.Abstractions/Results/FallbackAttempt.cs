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

using Boutquin.MarketData.Abstractions.Provenance;

namespace Boutquin.MarketData.Abstractions.Results;

/// <summary>
/// Records the outcome of a single adapter attempt during fallback orchestration.
/// </summary>
/// <param name="ProviderKey">The provider key of the adapter that was tried.</param>
/// <param name="Priority">The priority of the adapter at the time of the attempt.</param>
/// <param name="Succeeded">Whether the adapter produced a usable result.</param>
/// <param name="Outcome">A human-readable description of the attempt outcome (e.g., "Complete", "Partial — rejected", "Exception: timeout").</param>
/// <remarks>
/// The orchestrator accumulates one <see cref="FallbackAttempt"/> per adapter tried, whether it
/// succeeded, was rejected by the evaluator, or threw an exception. Consumers can inspect the
/// full attempt list to understand why a particular source was selected or skipped.
/// </remarks>
public sealed record FallbackAttempt(
    ProviderCode ProviderKey,
    int Priority,
    bool Succeeded,
    string Outcome);
