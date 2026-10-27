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

namespace Boutquin.MarketData.Abstractions.Observability;

/// <summary>
/// Classifies a <see cref="DataQualityIncident"/> by the kind of anomaly that produced it.
/// </summary>
public enum DataQualityIncidentKind
{
    /// <summary>
    /// A columnar row group failed its per-row-group CRC32C check on read and was quarantined
    /// by the storage engine.
    /// </summary>
    RowGroupQuarantine,

    /// <summary>
    /// An anomaly detected while ingesting adapter-sourced records into the time-series store
    /// (for example, out-of-range or duplicate dates) before they reach a consumer.
    /// </summary>
    IngestionAnomaly,
}

/// <summary>
/// An immutable description of a single data-quality event — a quarantined row group or an
/// ingestion anomaly — emitted by the pipeline to an <see cref="IDataQualityIncidentSink"/>
/// (or to <c>ILogger</c> when no sink is configured).
/// </summary>
/// <remarks>
/// <para><b>Lifecycle — emit and forget.</b> The pipeline constructs one incident per event,
/// hands it to the sink synchronously, and retains no reference. Retention and archival are
/// wholly the sink's concern; the pipeline holds no incident buffer and exposes no replay
/// surface through <c>DataQualityIncidentSink</c>.</para>
/// </remarks>
/// <param name="Kind">The category of anomaly that produced the incident.</param>
/// <param name="RecordType">
/// The dated record type whose read or ingestion produced the incident (for example
/// <c>nameof(Bar)</c>), used to scope the incident to a single store.
/// </param>
/// <param name="DatasetKey">
/// A human-readable rendering of the dataset partition key the incident applies to, or
/// <see langword="null"/> when the incident is not scoped to a single dataset.
/// </param>
/// <param name="OccurredAtUtc">The UTC instant at which the pipeline observed the incident.</param>
/// <param name="Detail">A free-form description of the anomaly suitable for an operator log line.</param>
public sealed record DataQualityIncident(
    DataQualityIncidentKind Kind,
    string RecordType,
    string? DatasetKey,
    DateTimeOffset OccurredAtUtc,
    string Detail);
