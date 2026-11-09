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

using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;

namespace Boutquin.MarketData.Testing.Builders;

/// <summary>Builds explicitly attributed price provenance for deterministic tests.</summary>
public static class TestProvenance
{
    /// <summary>Creates fixture provenance stamped with the delivered basis.</summary>
    /// <param name="provider">Provider identifier, normalized by ProviderCode.</param>
    /// <param name="basis">The delivered, validated price basis.</param>
    /// <returns>New immutable fixture provenance with the specified provider and basis.</returns>
    /// <exception cref="ArgumentException">The provider is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The basis is the invalid default.</exception>
    public static DataProvenance ForPriceDelivery(string provider, PriceBasis basis) =>
        new(new ProviderCode(provider), "price", LicenseType.Unknown, RetrievalMode.Fixture,
            FreshnessClass.Unknown, DateTimeOffset.UnixEpoch, null)
        { PriceBasis = basis };
}
