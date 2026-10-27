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

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Builds an <see cref="OptionQuoteDatasetKey"/> from an <see cref="OptionChainRequest"/>. Projects
/// <c>(Provider, Underlying)</c>; the per-contract discriminants <c>(Expiry, Strike, Type)</c> are not
/// request inputs (one request covers a whole chain), so they are left at their defaults.
/// </summary>
/// <param name="ProviderName">Adapter-assigned provider identifier (e.g. <c>"opra"</c>, <c>"polygon"</c>).</param>
/// <remarks>
/// <para><b>Per-contract discriminant placeholder:</b> an
/// <see cref="OptionChainRequest"/> addresses the whole option chain for one underlying over a date
/// range — the individual contract's <c>Expiry</c>/<c>Strike</c>/<c>Type</c> are per-row data, not a
/// request input. This single-key projection therefore leaves those three fields at their defaults
/// (<c>Expiry = default(DateOnly)</c>, <c>Strike = 0m</c>, <c>Type = OptionType.Call</c>), exactly as
/// the factor / futures-month / yield-tenor single-key builders leave their per-row discriminant
/// empty. Per-contract fan-out (one <see cref="OptionQuoteDatasetKey"/> per listed contract) is now
/// wired at the pipeline layer by the B′ keyed-row envelope
/// (<see cref="OptionChainRowFanOut"/>): the option-chain adapter emits per-contract fetch rows, and
/// the fan-out projects each to its own fully-populated key. This single-identity builder deliberately
/// remains the underlying-scoped prefix builder — <see cref="Build"/> cannot synthesise a
/// fully-qualified contract key from the placeholder request alone (contracts are response-discovered);
/// it builds the underlying-scoped prefix the chain read keys on.</para>
/// </remarks>
public sealed record OptionQuoteDatasetKeyBuilder(string ProviderName)
    : IDatasetKeyBuilder<OptionChainRequest, OptionQuoteDatasetKey>
{
    /// <inheritdoc />
    public OptionQuoteDatasetKey Build(OptionChainRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new OptionQuoteDatasetKey(
            ProviderName,
            request.Underlying,
            Expiry: default,
            Strike: 0m,
            Type: OptionType.Call);
    }
}
