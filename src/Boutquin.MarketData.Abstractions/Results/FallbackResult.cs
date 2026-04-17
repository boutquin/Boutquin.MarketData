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
/// The outcome of a fallback orchestration run, including the selected response and
/// an audit trail of all adapter attempts.
/// </summary>
/// <typeparam name="TResponse">The response type returned by the selected adapter.</typeparam>
/// <param name="Response">The usable response from the winning adapter.</param>
/// <param name="SelectedProviderKey">The provider key of the adapter that produced the accepted response.</param>
/// <param name="Attempts">All adapter attempts in the order they were tried, including rejected and failed ones.</param>
/// <remarks>
/// When more than one attempt was made, the consumer should record the fallback path
/// in provenance metadata. A single-attempt result indicates the primary source succeeded.
/// </remarks>
public sealed record FallbackResult<TResponse>(
    TResponse Response,
    ProviderCode SelectedProviderKey,
    IReadOnlyList<FallbackAttempt> Attempts);
