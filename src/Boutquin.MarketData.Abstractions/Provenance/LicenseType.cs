// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

namespace Boutquin.MarketData.Abstractions.Provenance;

/// <summary>
/// Closed set of license classifications carried on
/// <see cref="Results.DataProvenance.LicenseFlag"/>.
/// </summary>
/// <remarks>
/// <para>
/// Used by downstream audit and redistribution checks to decide whether a record can
/// be stored, forwarded, or published. The member set is intentionally finite — new
/// values are MarketData changes (not adapter changes) so that a consumer
/// <c>switch</c> can be exhaustive and the compiler flags missing arms under
/// <c>TreatWarningsAsErrors=true</c>.
/// </para>
/// <para>
/// Append-only ordinal contract: members are assigned explicit values and MUST NOT be
/// reordered or removed. New values are appended at the end of the enum.
/// </para>
/// </remarks>
public enum LicenseType
{
    /// <summary>
    /// Free redistribution permitted (e.g. Frankfurter, FRED public series).
    /// </summary>
    Free = 0,

    /// <summary>
    /// Delayed-by-15-minutes feed — typical of free tiers on commercial providers.
    /// </summary>
    DelayedFifteenMin = 1,

    /// <summary>
    /// Commercial license — consumer must have a provider agreement. No onward
    /// redistribution without the provider's explicit consent.
    /// </summary>
    Commercial = 2,

    /// <summary>
    /// Research / internal use only. Redistribution prohibited.
    /// </summary>
    ResearchOnly = 3,

    /// <summary>
    /// Redistribution permitted under the provider's stated terms (e.g. CC-BY sources,
    /// certain central-bank feeds).
    /// </summary>
    Redistribution = 4,

    /// <summary>
    /// Source license metadata is unavailable. Grants no redistribution permission; consumers
    /// must establish the applicable provider terms before using or forwarding the data.
    /// </summary>
    Unknown = 5,
}
