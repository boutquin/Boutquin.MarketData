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
/// Pluggable destination for <see cref="DataQualityIncident"/> events — quarantined row groups
/// and ingestion anomalies — raised by the time-series cache tier.
/// </summary>
/// <remarks>
/// <para><b>Default.</b> When no sink is registered, the pipeline emits incidents as structured
/// <c>ILogger</c> entries when <c>DataQualityIncidentSink</c> is <see langword="null"/>.
/// A consumer that needs incidents routed to a metrics pipeline, an alerting system, or a
/// durable audit store registers an implementation of this interface instead.</para>
/// <para><b>Contract.</b> <see cref="Report"/> is invoked synchronously from the read/ingest
/// path on the emitting thread; implementations must return promptly and must not throw — an
/// observability failure must never abort a data read. The pipeline retains no reference to the
/// incident after the call returns.</para>
/// </remarks>
public interface IDataQualityIncidentSink
{
    /// <summary>
    /// Records a single data-quality incident. Invoked once per incident, synchronously, from
    /// the emitting thread.
    /// </summary>
    /// <param name="incident">The incident to record. Never <see langword="null"/>.</param>
    void Report(DataQualityIncident incident);
}
