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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Results;

using Boutquin.Storage.Domain.Interfaces.Streaming;

namespace Boutquin.MarketData.Orchestration.Pipeline;

/// <summary>
/// Persists the time-series recovery facts that are not representable by stored rows: the
/// providers that have contributed to a logical request and the source-confirmed empty spans.
/// </summary>
/// <remarks>
/// The checkpoint store is the durable authority. The in-memory provider and attempted-range
/// indexes are deliberately only accelerators: their eviction cannot change restart recovery.
/// </remarks>
internal interface ITimeSeriesRecoveryMetadataStore
{
    Task<IReadOnlyList<TimeSeriesSourceMetadata>> GetSourcesAsync(
        string identity, DateRange range, CancellationToken cancellationToken = default);

    Task RecordSourceAsync(
        string identity, TimeSeriesSourceMetadata source, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderCode>> GetProvidersAsync(
        Type requestType,
        string datasetKey,
        CancellationToken cancellationToken = default);

    Task RecordProviderAsync(
        Type requestType,
        string datasetKey,
        ProviderCode provider,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(DateOnly From, DateOnly To)>> GetAttemptedRangesAsync(
        string datasetIdentity,
        CancellationToken cancellationToken = default);

    Task RecordAttemptAsync(
        string datasetIdentity,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Checkpoint-store-backed implementation of <see cref="ITimeSeriesRecoveryMetadataStore"/>.
/// </summary>
internal sealed class TimeSeriesRecoveryMetadataStore(ICheckpointStore checkpointStore)
    : ITimeSeriesRecoveryMetadataStore
{
    private const string Namespace = "timeseries-recovery.v1";
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public async Task<IReadOnlyList<TimeSeriesSourceMetadata>> GetSourcesAsync(
        string identity, DateRange range, CancellationToken cancellationToken = default)
    {
        var stored = await LoadAsync<SourceMetadata>(BuildProcessorId("sources", identity), cancellationToken)
            .ConfigureAwait(false);
        return stored?.Sources.Where(source => source.From <= range.To && source.To >= range.From).ToList() ?? [];
    }

    public async Task RecordSourceAsync(
        string identity, TimeSeriesSourceMetadata source, CancellationToken cancellationToken = default)
    {
        var processorId = BuildProcessorId("sources", identity);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stored = await LoadAsync<SourceMetadata>(processorId, cancellationToken).ConfigureAwait(false);
            // Retain overlapping source facts conservatively: a later fill can leave earlier
            // provider rows in the logical series, so overwriting their license/issues is unsafe.
            await SaveAsync(processorId, new SourceMetadata([.. stored?.Sources ?? [], source]), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<ProviderCode>> GetProvidersAsync(
        Type requestType,
        string datasetKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(datasetKey);

        var stored = await LoadAsync<ProviderMetadata>(
            BuildProcessorId("providers", RequestIdentity(requestType, datasetKey)),
            cancellationToken).ConfigureAwait(false);
        return stored?.Providers.Select(static provider => new ProviderCode(provider)).ToList() ?? [];
    }

    public async Task RecordProviderAsync(
        Type requestType,
        string datasetKey,
        ProviderCode provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(datasetKey);

        var processorId = BuildProcessorId("providers", RequestIdentity(requestType, datasetKey));
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await LoadAsync<ProviderMetadata>(processorId, cancellationToken).ConfigureAwait(false);
            var providers = existing?.Providers ?? [];
            if (providers.Count > 0 && string.Equals(providers[^1], provider.Value, StringComparison.Ordinal))
            {
                return;
            }

            await SaveAsync(
                processorId,
                // Recovery uses the last contributor as the selected provider. Move a
                // reselected contributor to the tail without forgetting any other source.
                new ProviderMetadata([.. providers.Where(value => !string.Equals(value, provider.Value, StringComparison.Ordinal)), provider.Value]),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<IReadOnlyList<(DateOnly From, DateOnly To)>> GetAttemptedRangesAsync(
        string datasetIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datasetIdentity);

        var stored = await LoadAsync<AttemptMetadata>(
            BuildProcessorId("attempts", datasetIdentity),
            cancellationToken).ConfigureAwait(false);
        return stored?.Ranges
            .Select(static range => (DateOnly.FromDayNumber(range.FromDayNumber), DateOnly.FromDayNumber(range.ToDayNumber)))
            .ToList() ?? [];
    }

    public async Task RecordAttemptAsync(
        string datasetIdentity,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datasetIdentity);

        var processorId = BuildProcessorId("attempts", datasetIdentity);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await LoadAsync<AttemptMetadata>(processorId, cancellationToken).ConfigureAwait(false);
            var ranges = existing?.Ranges
                .Select(static range => (DateOnly.FromDayNumber(range.FromDayNumber), DateOnly.FromDayNumber(range.ToDayNumber)))
                .ToList() ?? [];
            ranges.Add(from <= to ? (from, to) : (to, from));

            var merged = Merge(ranges)
                .Select(static range => new StoredRange(range.From.DayNumber, range.To.DayNumber))
                .ToList();
            await SaveAsync(processorId, new AttemptMetadata(merged), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<TMetadata?> LoadAsync<TMetadata>(string processorId, CancellationToken cancellationToken)
    {
        var checkpoint = await checkpointStore.LoadAsync(processorId, cancellationToken).ConfigureAwait(false);
        if (checkpoint is null)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<TMetadata>(checkpoint.Value.State)
                ?? throw new InvalidDataException($"Recovery metadata '{processorId}' deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Recovery metadata '{processorId}' is corrupted.", ex);
        }
    }

    private Task SaveAsync<TMetadata>(string processorId, TMetadata metadata, CancellationToken cancellationToken)
        => checkpointStore.SaveAsync(
            processorId,
            offset: 0,
            JsonSerializer.SerializeToUtf8Bytes(metadata),
            cancellationToken).AsTask();

    private static string RequestIdentity(Type requestType, string datasetKey)
        => $"{requestType.AssemblyQualifiedName}\u001f{datasetKey}";

    private static string BuildProcessorId(string category, string identity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"{Namespace}.{category}.{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static IReadOnlyList<(DateOnly From, DateOnly To)> Merge(
        List<(DateOnly From, DateOnly To)> ranges)
    {
        ranges.Sort(static (left, right) => left.From.CompareTo(right.From));
        var merged = new List<(DateOnly From, DateOnly To)>(ranges.Count);
        var current = ranges[0];
        for (var i = 1; i < ranges.Count; i++)
        {
            var next = ranges[i];
            if (next.From.DayNumber <= current.To.DayNumber + 1)
            {
                current = (current.From, next.To > current.To ? next.To : current.To);
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    private sealed record ProviderMetadata(IReadOnlyList<string> Providers);

    private sealed record SourceMetadata(IReadOnlyList<TimeSeriesSourceMetadata> Sources);

    private sealed record AttemptMetadata(IReadOnlyList<StoredRange> Ranges);

    private sealed record StoredRange(int FromDayNumber, int ToDayNumber);
}

internal sealed record TimeSeriesSourceMetadata(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DataIssue> Issues,
    IReadOnlyList<DataProvenance> Provenance);
