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

namespace Boutquin.MarketData.Abstractions.Contracts;

/// <summary>
/// Durable snapshot storage for persisting and retrieving data snapshots by key.
/// </summary>
/// <remarks>
/// Snapshots provide reproducibility: once written, a snapshot captures the exact data
/// state for a given key. Implementations must guarantee that concurrent reads never
/// observe a partial write. The file-system implementation achieves this via atomic
/// temp-file-plus-rename.
/// </remarks>
public interface ISnapshotStore
{
    /// <summary>
    /// Checks whether a snapshot with the specified key exists in the store.
    /// </summary>
    /// <param name="snapshotKey">The unique key identifying the snapshot.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> if the snapshot exists; otherwise, <see langword="false"/>.</returns>
    Task<bool> ExistsAsync(string snapshotKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the snapshot content as a stream, or returns <see langword="null"/> if not found.
    /// </summary>
    /// <param name="snapshotKey">The unique key identifying the snapshot.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Stream"/> containing the snapshot data, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The returned <see cref="Stream"/> is owned by the caller and MUST be disposed
    /// (preferably via <c>await using</c>) — implementations may back it with a file
    /// handle or network connection whose resources leak until the Stream is disposed.
    /// </remarks>
    Task<Stream?> ReadAsync(string snapshotKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes snapshot content to the store, overwriting any existing snapshot with the same key.
    /// </summary>
    /// <param name="snapshotKey">The unique key identifying the snapshot.</param>
    /// <param name="content">The stream containing the snapshot data to persist.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task WriteAsync(string snapshotKey, Stream content, CancellationToken cancellationToken = default);
}
