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
using Boutquin.Storage.Domain.ValueObjects.LsmTree;

namespace Boutquin.MarketData.DependencyInjection;

/// <summary>Turns a Storage corrupt-segment exclusion into a <see cref="DataQualityIncident"/>.</summary>
internal static class SegmentExclusionReporter
{
    /// <summary>
    /// Reports the exclusion to <paramref name="sink"/>. An observability failure must never abort
    /// engine open, so a throwing sink is swallowed.
    /// </summary>
    internal static void Report(IDataQualityIncidentSink? sink, string recordType, CorruptSegmentExclusion exclusion)
    {
        if (sink is null)
        {
            return;
        }

        var detail = exclusion.FileMissing
            ? $"Segment {exclusion.FileName} is missing; its span is treated as a gap and will be re-fetched."
            : $"Segment {exclusion.FileName} failed its integrity check (manifest CRC32C {exclusion.ManifestCrc32C:X8}, "
              + $"observed {exclusion.ObservedCrc32C:X8}); its span is treated as a gap and will be re-fetched.";
        try
        {
            sink.Report(new DataQualityIncident(
                DataQualityIncidentKind.SegmentExclusion, recordType, null, DateTimeOffset.UtcNow, detail));
        }
        catch (Exception)
        {
            // The sink contract forbids throwing; a faulty sink must not fail the store open.
        }
    }
}
