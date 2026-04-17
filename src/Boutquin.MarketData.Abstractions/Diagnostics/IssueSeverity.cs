// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

namespace Boutquin.MarketData.Abstractions.Diagnostics;

/// <summary>
/// Binary severity classification carried on <see cref="Results.DataIssue"/>.
/// </summary>
/// <remarks>
/// Severity is a fixed binary in MarketData's diagnostic contract — a recoverable gap
/// (<see cref="Warning"/>) or data the consumer should not trust (<see cref="Error"/>).
/// An enum rather than a record struct because the member set is exhaustive and
/// consumers must be able to <c>switch</c> on it without a default arm.
/// </remarks>
public enum IssueSeverity
{
    /// <summary>
    /// Recoverable gap — the envelope is usable but incomplete or noteworthy.
    /// Examples: a single missing trading day inside a longer range, a delayed tick
    /// relative to expected freshness.
    /// </summary>
    Warning = 0,

    /// <summary>
    /// The consumer should not trust the data in this envelope for the range or
    /// partition flagged. Examples: partition fetch failed, schema drift detected,
    /// rate limiter gave up before a full response was assembled.
    /// </summary>
    Error = 1,
}
