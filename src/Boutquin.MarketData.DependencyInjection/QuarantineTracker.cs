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

using System.Collections.Concurrent;

using Boutquin.MarketData.Orchestration.Pipeline;

namespace Boutquin.MarketData.DependencyInjection;

internal sealed class QuarantineTracker
{
    private const int IncidentCapacity = 1_024;
    private readonly ConcurrentDictionary<string, byte> _incidents = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _incidentOrder = new();
    private readonly object _recoveryGate = new();
    private readonly Dictionary<string, RecoveryOwner> _recoveries = new(StringComparer.Ordinal);

    public bool TryRecord(
        int rowGroupIndex,
        uint expectedCrc,
        uint actualCrc,
        DateRange range,
        bool recover,
        string segmentIdentity = "")
    {
        ArgumentNullException.ThrowIfNull(segmentIdentity);
        var fingerprint = $"{segmentIdentity}:{rowGroupIndex}:{expectedCrc:x8}:{actualCrc:x8}";
        var firstObservation = _incidents.TryAdd(fingerprint, 0);
        if (firstObservation)
        {
            _incidentOrder.Enqueue(fingerprint);
            while (_incidents.Count > IncidentCapacity && _incidentOrder.TryDequeue(out var expired))
            {
                _incidents.TryRemove(expired, out _);
            }
        }

        var scope = QuarantineRecoveryScope.Current;
        if (recover && scope is not null && TryClaimRecovery(fingerprint, scope))
        {
            scope.Record(range);
        }

        return firstObservation;
    }

    public IReadOnlyList<DateRange> Drain() => QuarantineRecoveryScope.Drain();

    private bool TryClaimRecovery(string fingerprint, QuarantineRecoveryScope.Scope scope)
    {
        lock (_recoveryGate)
        {
            if (_recoveries.TryGetValue(fingerprint, out var existing))
            {
                // A shared segment can be authenticated while an unrelated dataset request is
                // reading. That observer must not recover another request's range. Concurrent
                // requests for the SAME logical dataset do each need their own bounded recovery.
                if (!StringComparer.Ordinal.Equals(existing.OwnerIdentity, scope.OwnerIdentity))
                {
                    return false;
                }

                existing.ClaimCount++;
            }
            else
            {
                existing = new RecoveryOwner(scope.OwnerIdentity);
                _recoveries.Add(fingerprint, existing);
            }

            scope.RegisterRelease(() => ReleaseRecovery(fingerprint, existing));
            return true;
        }
    }

    private void ReleaseRecovery(string fingerprint, RecoveryOwner owner)
    {
        lock (_recoveryGate)
        {
            if (--owner.ClaimCount == 0
                && _recoveries.TryGetValue(fingerprint, out var current)
                && ReferenceEquals(current, owner))
            {
                _recoveries.Remove(fingerprint);
            }
        }
    }

    // Direct segment-format tests enter the same request-scoped collector explicitly. Direct reads
    // have no pipeline owner, so they must not leave recoverable work in a process-wide queue.
    internal IDisposable EnterRecoveryScope() => QuarantineRecoveryScope.Enter("direct-format-read");

    private sealed class RecoveryOwner(string ownerIdentity)
    {
        public string OwnerIdentity { get; } = ownerIdentity;

        public int ClaimCount { get; set; } = 1;
    }
}
