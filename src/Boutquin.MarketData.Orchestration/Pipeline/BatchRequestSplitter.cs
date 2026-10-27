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

using System.Collections;

using Boutquin.MarketData.Abstractions.Contracts;

namespace Boutquin.MarketData.Orchestration.Pipeline;

/// <summary>
/// Splits an <see cref="IBatchDataRequest{TPartitionKey}"/> that carries more than one partition key
/// into one single-partition request per key, so the time-series flow can build one dataset key per
/// partition instead of collapsing every partition onto <c>PartitionKeys[0]</c>.
/// </summary>
/// <remarks>
/// <para>
/// The dataset-key builders (<c>PriceBarDatasetKeyBuilder</c>, <c>FxRateDatasetKeyBuilder</c>) are
/// documented single-identity operations: their own remarks state "the pipeline partitions a
/// multi-symbol / multi-pair batch into one single-partition request per partition BEFORE reaching the
/// time-series store." <c>DataPipeline.ExecuteAsync</c> runs the time-series branch (Step 2b) ahead of
/// the legacy Step 5 partition, so the store path never inherited that partition step; this splitter
/// supplies it. A multi-symbol batch would otherwise key every symbol on <c>Symbols[0]</c> (silent
/// cross-attribution), and a multi-pair FX batch would throw the single-pair guard in
/// <c>FxRateDatasetKeyBuilder.Build</c>.
/// </para>
/// <para>
/// The split is done reflectively rather than through a per-type interface method so that the request
/// types (in the Abstractions package) need no change. It reuses the record's compiler-generated clone
/// and overwrites the single partition-key-bearing list property — the same property whose value backs
/// <see cref="IBatchDataRequest{TPartitionKey}.PartitionKeys"/>.
/// </para>
/// </remarks>
internal static class BatchRequestSplitter
{
    // The compiler-generated record clone method (the target of a `with` expression). Records emit it
    // as a public method with this unspeakable name; reflection reaches it where C# source cannot.
    private const string RecordCloneMethodName = "<Clone>$";

    /// <summary>
    /// Attempts to split <paramref name="request"/> into one single-partition request per partition key.
    /// </summary>
    /// <typeparam name="TRequest">The concrete request type (preserved on every produced part).</typeparam>
    /// <param name="request">The request to split.</param>
    /// <param name="parts">
    /// On <see langword="true"/>, one request per partition key, each carrying exactly one key and every
    /// other field of <paramref name="request"/> unchanged. Empty on <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="request"/> is a batch request with more than one
    /// partition key (so a split was produced); <see langword="false"/> when it is not a batch request
    /// or carries at most one partition key — in which case the caller uses the request unchanged and
    /// today's single-partition behaviour is preserved exactly.
    /// </returns>
    public static bool TrySplit<TRequest>(TRequest request, out IReadOnlyList<TRequest> parts)
        where TRequest : IDataRequest
    {
        parts = [];

        var requestType = request!.GetType();
        var batchInterface = Array.Find(
            requestType.GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBatchDataRequest<>));
        if (batchInterface is null)
        {
            return false;
        }

        var partitionKeysValue = batchInterface
            .GetProperty(nameof(IBatchDataRequest<object>.PartitionKeys))!
            .GetValue(request);
        if (partitionKeysValue is not IEnumerable keysEnumerable)
        {
            return false;
        }

        var keys = keysEnumerable.Cast<object>().ToList();
        if (keys.Count <= 1)
        {
            // Single-partition (or empty) batch — no split; behave exactly as before.
            return false;
        }

        // The list property whose value backs PartitionKeys (e.g. Symbols / Pairs). It is the single
        // writable instance property whose current value is the very PartitionKeys collection object.
        var partitionListProperty = Array.Find(
            requestType.GetProperties(),
            p => p.CanWrite && ReferenceEquals(p.GetValue(request), partitionKeysValue))
            ?? throw new InvalidOperationException(
                $"Cannot partition {requestType.Name} for the time-series store: no writable list "
                + "property backs IBatchDataRequest.PartitionKeys. Override the split, register an "
                + "IRequestPartitioner, or route the request through ExecutePartitionedAsync.");

        var elementType = batchInterface.GetGenericArguments()[0];
        var cloneMethod = requestType.GetMethod(RecordCloneMethodName)
            ?? throw new InvalidOperationException(
                $"Cannot partition {requestType.Name} for the time-series store: the type is not a "
                + "record (no compiler-generated clone). Route it through ExecutePartitionedAsync.");

        var result = new List<TRequest>(keys.Count);
        foreach (var key in keys)
        {
            var singleKeyList = Array.CreateInstance(elementType, 1);
            singleKeyList.SetValue(key, 0);

            var clone = (TRequest)cloneMethod.Invoke(request, null)!;
            partitionListProperty.SetValue(clone, singleKeyList);
            result.Add(clone);
        }

        parts = result;
        return true;
    }
}
