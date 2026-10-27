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
namespace Boutquin.MarketData.Catalog.Governance;

/// <summary>
/// Encodes the licensing posture and redistribution constraints of a data source,
/// for application-level source-selection policies.
/// </summary>
/// <remarks>
/// These flags are descriptive metadata; the kernel does not enforce usage restrictions
/// from them. <see cref="Abstractions.Results.DataProvenance"/> carries its own license type.
/// Applications must explicitly map and enforce the applicable source terms.
/// </remarks>
public enum DataLicenseFlag
{
    /// <summary>
    /// Data is public domain or equivalent — no attribution or licensing constraints.
    /// </summary>
    PublicDomainLike,

    /// <summary>
    /// Data is freely available but requires source attribution in derived outputs.
    /// </summary>
    PublicWithAttribution,

    /// <summary>
    /// Data is publicly visible on the provider website but not licensed for programmatic
    /// consumption or redistribution. Use for research reference only.
    /// </summary>
    WebsiteViewOnly,

    /// <summary>
    /// Data is available free of charge but delayed (e.g., 15 minutes, end-of-day previous close).
    /// Suitable for research and backtesting but not real-time production use.
    /// </summary>
    DelayedFree,

    /// <summary>
    /// Data requires a benchmark or index license for commercial use (e.g., ICE LIBOR,
    /// CME Term SOFR). Internal research may be permitted under fair-use provisions.
    /// </summary>
    BenchmarkLicenseRequired,

    /// <summary>
    /// Data requires an active commercial subscription for any use. Redistribution
    /// is typically prohibited without an explicit redistribution agreement.
    /// </summary>
    CommercialSubscription,

    /// <summary>
    /// Data is restricted to internal use only. May not be shared, published, or
    /// used in client-facing outputs.
    /// </summary>
    InternalOnly,
}
