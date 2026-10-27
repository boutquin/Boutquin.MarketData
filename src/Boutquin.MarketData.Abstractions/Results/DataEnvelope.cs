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

namespace Boutquin.MarketData.Abstractions.Results;

/// <summary>
/// Wraps a data payload with coverage metrics, quality issues, and provenance metadata.
/// </summary>
/// <typeparam name="TPayload">The type of the data payload.</typeparam>
/// <param name="Payload">The fetched data records.</param>
/// <param name="Coverage">Coverage metrics describing data completeness.</param>
/// <param name="Issues">Any data quality issues encountered during fetch or normalization.</param>
/// <param name="Provenance">Provenance records describing the source and retrieval context of the data.</param>
/// <remarks>
/// The envelope is the standard return type for all pipeline and adapter operations. Consumers
/// should always inspect <see cref="Issues"/> before using the payload — a non-empty issue list
/// with severity "Error" indicates that the payload may be incomplete or unreliable.
/// Multiple provenance entries appear when the pipeline merged results from fallback adapters.
/// </remarks>
public sealed record DataEnvelope<TPayload>(
    TPayload Payload,
    DataCoverage Coverage,
    IReadOnlyList<DataIssue> Issues,
    IReadOnlyList<DataProvenance> Provenance)
{
    /// <summary>
    /// Data quality issues, copied on construction and initialization and exposed read-only.
    /// </summary>
    public IReadOnlyList<DataIssue> Issues
    {
        get;
        init => field = Array.AsReadOnly(value.ToArray());
    } = Array.AsReadOnly(Issues.ToArray());

    /// <summary>
    /// Source and retrieval metadata, copied on construction and initialization and exposed read-only.
    /// </summary>
    public IReadOnlyList<DataProvenance> Provenance
    {
        get;
        init => field = Array.AsReadOnly(value.ToArray());
    } = Array.AsReadOnly(Provenance.ToArray());
}
