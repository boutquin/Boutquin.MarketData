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

namespace Boutquin.MarketData.Orchestration.Pipeline;

/// <summary>Collects quarantine ranges for one logical store-flow execution.</summary>
internal static class QuarantineRecoveryScope
{
    private static readonly AsyncLocal<Scope?> s_current = new();

    /// <summary>Enters a recovery scope that flows with the current asynchronous request.</summary>
    /// <param name="ownerIdentity">The range-independent logical data identity that owns recovery work.</param>
    public static IDisposable Enter(string ownerIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerIdentity);

        var current = new Scope(ownerIdentity);
        var previous = s_current.Value;
        s_current.Value = current;
        return new Lease(current, previous);
    }

    /// <summary>Gets the request-owned collector for the current asynchronous context.</summary>
    internal static Scope? Current => s_current.Value;

    /// <summary>Drains the current flow's ranges without consuming another flow's recovery work.</summary>
    public static IReadOnlyList<DateRange> Drain()
    {
        var current = s_current.Value;
        return current?.Drain() ?? [];
    }

    /// <summary>One request's bounded recovery collector and cleanup registration point.</summary>
    internal sealed class Scope
    {
        private readonly ConcurrentQueue<Action> _releaseActions = new();

        internal Scope(string ownerIdentity) => OwnerIdentity = ownerIdentity;

        private ConcurrentQueue<DateRange> Ranges { get; } = new();

        /// <summary>Gets the logical data identity permitted to own this scope's recovery work.</summary>
        public string OwnerIdentity { get; }

        /// <summary>Adds a request-bounded range to this collector.</summary>
        public void Record(DateRange range) => Ranges.Enqueue(range);

        /// <summary>Registers tracker-local state that must end with this request.</summary>
        public void RegisterRelease(Action release) => _releaseActions.Enqueue(release);

        /// <summary>Drains only this request's recovery ranges.</summary>
        public IReadOnlyList<DateRange> Drain()
        {
            var ranges = new List<DateRange>();
            while (Ranges.TryDequeue(out var range))
            {
                ranges.Add(range);
            }

            return ranges;
        }

        /// <summary>Releases all tracker-local ownership claimed by this request.</summary>
        public void ReleaseClaims()
        {
            while (_releaseActions.TryDequeue(out var release))
            {
                release();
            }
        }
    }

    private sealed class Lease(Scope current, Scope? previous) : IDisposable
    {
        private Scope? _current = current;
        private Scope? _previous = previous;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _current, null);
            if (current is null)
            {
                return;
            }

            current.ReleaseClaims();
            if (ReferenceEquals(s_current.Value, current))
            {
                s_current.Value = _previous;
            }

            _previous = null;
        }
    }
}
