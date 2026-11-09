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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Exceptions;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;

namespace Boutquin.MarketData.Orchestration.Pipeline;

// All checks are stateless. A null entry does not state a basis; at least one statement is
// required, and every stated basis must match. Non-price request/record pairs are unchanged.
internal static class PriceBasisGuard
{
    internal static void EnsureDeclared<TRequest, TRecord>(TRequest request,
        IReadOnlyList<IDataSourceAdapter<TRequest, TRecord>> adapters) where TRequest : IDataRequest
    {
        if (request is not PriceHistoryRequest prices || typeof(TRecord) != typeof(Bar)) { return; }
        var declared = adapters.OfType<IPriceBasisAdapter>().Select(adapter => adapter.DeliveredBasis).Distinct().ToArray();
        if (!declared.Contains(prices.Basis))
        {
            throw new PriceBasisNotSupportedException(prices.Basis, declared);
        }
    }

    internal static bool TryAccept<TRequest, TRecord>(TRequest request, DataEnvelope<IReadOnlyList<TRecord>> envelope,
        out string? rejection) where TRequest : IDataRequest
    {
        rejection = null;
        if (request is not PriceHistoryRequest prices || typeof(TRecord) != typeof(Bar)) { return true; }
        var mismatch = envelope.Provenance.FirstOrDefault(p => p.PriceBasis is { } basis && basis != prices.Basis);
        if (mismatch is not null)
        {
            rejection = $"price basis {mismatch.PriceBasis!.Value.Token} ≠ {prices.Basis.Token}";
            return false;
        }
        if (!envelope.Provenance.Any(p => p.PriceBasis is not null))
        {
            rejection = "price basis unproven";
            return false;
        }
        return true;
    }

    internal static bool CanReplay<TRecord>(IDataRequest request, IReadOnlyList<DataProvenance> provenance)
    {
        if (request is not PriceHistoryRequest prices || typeof(TRecord) != typeof(Bar)) { return true; }
        var mismatch = provenance.FirstOrDefault(p => p.PriceBasis is { } basis && basis != prices.Basis);
        if (mismatch is not null)
        {
            throw new PriceBasisMismatchException(prices.Basis, mismatch.PriceBasis, mismatch.Provider.Value);
        }
        return provenance.Any(p => p.PriceBasis is not null);
    }

    internal static void EnsurePartitionWindows<TRequest, TRecord>(TRequest request, IReadOnlyList<TRequest> partitions)
    {
        if (request is not PriceHistoryRequest prices || typeof(TRecord) != typeof(Bar))
        {
            return;
        }

        // Every basis: a partition that rewrites the basis would be fetched, cached and returned
        // under the parent's basis-qualified keys, whatever the partition count.
        var rewritten = partitions.OfType<PriceHistoryRequest>().FirstOrDefault(partition => partition.Basis != prices.Basis);
        if (rewritten is not null)
        {
            throw new PriceBasisMismatchException(prices.Basis, rewritten.Basis, "partition");
        }

        if (prices.Basis.LevelsAreWindowIndependent)
        {
            return;
        }

        if (partitions.OfType<PriceHistoryRequest>().Any(partition => partition.Range != prices.Range))
        {
            throw new InvalidOperationException("Adjusted price partitions must preserve the original request window; independently anchored levels cannot be concatenated.");
        }
    }

    internal static void EnsureMerged(PriceHistoryRequest request, IReadOnlyList<DataProvenance> provenance)
    {
        if (!CanReplay<Bar>(request, provenance))
        {
            throw new PriceBasisMismatchException(request.Basis, null, "partition-merge");
        }
    }
}
