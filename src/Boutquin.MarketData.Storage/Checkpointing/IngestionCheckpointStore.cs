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
using System.Text.Json;

using Boutquin.Storage.Domain.Interfaces.Streaming;

namespace Boutquin.MarketData.Storage.Checkpointing;

/// <summary>
/// Typed wrapper around <see cref="ICheckpointStore"/> for ingestion pipeline checkpointing.
/// Serializes <see cref="IngestionCheckpoint"/> to/from the opaque state blob.
/// </summary>
/// <remarks>
/// Each pipeline is identified by a unique <c>pipelineId</c> string (e.g., "tiingo-daily").
/// The <see cref="IngestionCheckpoint.SymbolIndex"/> is stored as the checkpoint offset,
/// enabling the orchestrator to resume from the last successfully processed symbol.
/// </remarks>
public sealed class IngestionCheckpointStore
{
    private readonly ICheckpointStore _inner;

    /// <summary>
    /// Initializes a new <see cref="IngestionCheckpointStore"/>.
    /// </summary>
    /// <param name="inner">The backing checkpoint store.</param>
    public IngestionCheckpointStore(ICheckpointStore inner) => _inner = inner;

    /// <summary>
    /// Persists the current ingestion checkpoint for the specified pipeline.
    /// </summary>
    /// <param name="pipelineId">Unique identifier for the ingestion pipeline.</param>
    /// <param name="checkpoint">The checkpoint state to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SaveAsync(string pipelineId, IngestionCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(checkpoint);
        await _inner.SaveAsync(pipelineId, checkpoint.SymbolIndex, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the last saved checkpoint for the specified pipeline.
    /// </summary>
    /// <param name="pipelineId">Unique identifier for the ingestion pipeline.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The checkpoint, or <c>null</c> if no checkpoint exists.</returns>
    public async Task<IngestionCheckpoint?> LoadAsync(string pipelineId, CancellationToken cancellationToken = default)
    {
        var result = await _inner.LoadAsync(pipelineId, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return null;
        }

        IngestionCheckpoint? checkpoint;
        try
        {
            checkpoint = JsonSerializer.Deserialize<IngestionCheckpoint>(result.Value.State);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Checkpoint for pipeline '{pipelineId}' could not be deserialized — the stored state is corrupted.",
                ex);
        }

        if (checkpoint is null)
        {
            throw new InvalidDataException(
                $"Checkpoint for pipeline '{pipelineId}' deserialized to null (stored state was JSON null).");
        }

        // Defense-in-depth: a poisoned checkpoint with bogus fields would silently distort the
        // orchestrator's resume index. Validate the invariants the pipeline relies on before
        // returning.
        if (checkpoint.SymbolIndex < 0)
        {
            throw new InvalidDataException(
                $"Checkpoint for pipeline '{pipelineId}' has negative SymbolIndex ({checkpoint.SymbolIndex}).");
        }

        if (string.IsNullOrWhiteSpace(checkpoint.Provider))
        {
            throw new InvalidDataException(
                $"Checkpoint for pipeline '{pipelineId}' has empty Provider.");
        }

        if (string.IsNullOrWhiteSpace(checkpoint.Dataset))
        {
            throw new InvalidDataException(
                $"Checkpoint for pipeline '{pipelineId}' has empty Dataset.");
        }

        return checkpoint;
    }
}
