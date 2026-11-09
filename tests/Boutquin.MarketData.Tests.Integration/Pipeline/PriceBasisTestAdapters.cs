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
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;

namespace Boutquin.MarketData.Tests.Integration.Pipeline;

// Existing fixture responses predate the declaration contract. This explicit decorator keeps
// their original payload/coverage/issues and priority, and declares the fixture's chosen basis.
internal sealed class DeclaringPriceAdapter(IDataSourceAdapter<PriceHistoryRequest, Bar> inner, PriceBasis basis) :
    IDataSourceAdapter<PriceHistoryRequest, Bar>, IPrioritizedAdapter, IPriceBasisAdapter
{
    public ProviderCode ProviderKey => inner.ProviderKey;
    public int Priority => inner is IPrioritizedAdapter prioritized ? prioritized.Priority : 0;
    public PriceBasis DeliveredBasis => basis;
    public bool CanHandle(PriceHistoryRequest request) => inner.CanHandle(request) && request.Basis == basis;
    public async Task<DataEnvelope<IReadOnlyList<Bar>>> FetchAsync(PriceHistoryRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Basis != basis) { throw new PriceBasisNotSupportedException(request.Basis, [basis]); }
        var result = await inner.FetchAsync(request, cancellationToken);
        return new(result.Payload, result.Coverage, result.Issues, result.Provenance.Select(p => p with { PriceBasis = basis }).ToArray());
    }
}
