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

using Boutquin.MarketData.Abstractions.Observability;

using Microsoft.Extensions.Logging;

namespace Boutquin.MarketData.DependencyInjection;

/// <summary>
/// Emit helpers for <see cref="DataQualityIncident"/> that implement the default-sink
/// contract: route to the configured <see cref="IDataQualityIncidentSink"/> when present,
/// otherwise fall back to a structured <c>ILogger</c> entry.
/// </summary>
public static class DataQualityIncidentSinkExtensions
{
    /// <summary>
    /// Reports <paramref name="incident"/> to <paramref name="sink"/> when it is non-null;
    /// otherwise logs it as a structured warning via <paramref name="logger"/>. Never throws on
    /// the null-sink path — an observability failure must not abort a data read.
    /// </summary>
    /// <param name="sink">The configured incident sink, or <see langword="null"/> for the logger fallback.</param>
    /// <param name="incident">The incident to record. Never <see langword="null"/>.</param>
    /// <param name="logger">The fallback logger used when <paramref name="sink"/> is <see langword="null"/>.</param>
    public static void ReportOrLog(
        this IDataQualityIncidentSink? sink,
        DataQualityIncident incident,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(logger);

        if (sink is not null)
        {
            sink.Report(incident);
            return;
        }

        logger.LogWarning(
            "Data-quality incident {Kind} on {RecordType} (dataset {DatasetKey}) at {OccurredAtUtc:o}: {Detail}",
            incident.Kind,
            incident.RecordType,
            incident.DatasetKey ?? "<none>",
            incident.OccurredAtUtc,
            incident.Detail);
    }
}
