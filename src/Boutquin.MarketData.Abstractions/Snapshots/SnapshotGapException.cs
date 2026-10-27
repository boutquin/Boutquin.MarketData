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

namespace Boutquin.MarketData.Abstractions.Snapshots;

/// <summary>
/// Thrown by <see cref="IDataSnapshot.ExecuteAsync{TRecord}"/> when a requested range is not fully
/// covered by the captured point-in-time view. A snapshot read fails fast on a gap rather than
/// dispatching an adapter: fetching would both violate the snapshot's stability guarantee
/// and leave the newly appended records invisible to the same snapshot.
/// </summary>
/// <remarks>
/// Catch this to fall back to a live <c>IDataPipeline.ExecuteAsync</c> read with different
/// (non-isolated) semantics, or to surface the missing range to the caller. The exception does
/// not partially populate any cache — the snapshot remains usable for other record types and
/// other ranges.
/// </remarks>
public sealed class SnapshotGapException : Exception
{
    /// <summary>Initialises a new <see cref="SnapshotGapException"/>.</summary>
    public SnapshotGapException()
        : base("The requested range is not fully covered by the read snapshot. "
            + "A snapshot read fails fast on a gap and never dispatches an adapter.")
    {
    }

    /// <summary>Initialises a new <see cref="SnapshotGapException"/> with a specific message.</summary>
    /// <param name="message">The message describing the uncovered range.</param>
    public SnapshotGapException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises a new <see cref="SnapshotGapException"/> with a message and inner exception.</summary>
    /// <param name="message">The message describing the uncovered range.</param>
    /// <param name="innerException">The underlying cause, if any.</param>
    public SnapshotGapException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
