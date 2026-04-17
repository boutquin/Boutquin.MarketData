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

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>
/// Carries correlation and timing metadata through the pipeline for distributed tracing and audit trails.
/// </summary>
/// <param name="CorrelationId">A unique identifier linking all pipeline operations for a single logical request.</param>
/// <param name="RequestedAtUtc">The UTC timestamp when the caller initiated the request.</param>
/// <remarks>
/// The pipeline creates a <see cref="RequestContext"/> at the start of each
/// <see cref="Contracts.IDataPipeline.ExecuteAsync{TRequest,TRecord}"/> call. Adapters,
/// normalizers, and cache layers can log the <see cref="CorrelationId"/> to correlate
/// their activity with the originating request. When debugging multi-adapter fetches,
/// the correlation ID links all provenance entries back to a single pipeline invocation.
/// </remarks>
public sealed record RequestContext(Guid CorrelationId, DateTimeOffset RequestedAtUtc)
{
    /// <summary>
    /// Creates a new context with a fresh correlation ID and the current UTC time.
    /// </summary>
    public static RequestContext Create() => new(Guid.NewGuid(), DateTimeOffset.UtcNow);
}
