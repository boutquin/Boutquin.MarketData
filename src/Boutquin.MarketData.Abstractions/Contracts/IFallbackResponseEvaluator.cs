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

namespace Boutquin.MarketData.Abstractions.Contracts;

/// <summary>
/// Determines whether a response from a data source adapter is usable, driving fallback decisions.
/// </summary>
/// <typeparam name="TResponse">The response type to evaluate.</typeparam>
/// <remarks>
/// The fallback orchestrator calls this evaluator after each successful adapter fetch.
/// If <see cref="IsUsable"/> returns <see langword="false"/>, the orchestrator proceeds
/// to the next adapter. Implementations can enforce coverage thresholds, license constraints,
/// or freshness requirements.
/// </remarks>
public interface IFallbackResponseEvaluator<in TResponse>
{
    /// <summary>
    /// Evaluates whether the response meets acceptance criteria.
    /// </summary>
    /// <param name="response">The adapter response to evaluate.</param>
    /// <param name="reason">When returning <see langword="false"/>, a description of why the response was rejected.</param>
    /// <returns><see langword="true"/> if the response is usable; otherwise, <see langword="false"/>.</returns>
    bool IsUsable(TResponse response, out string? reason);
}
