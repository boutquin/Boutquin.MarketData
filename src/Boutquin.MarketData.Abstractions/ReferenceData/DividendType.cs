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

namespace Boutquin.MarketData.Abstractions.ReferenceData;

/// <summary>
/// Classifies dividend income by the issuer's jurisdictional category.
/// </summary>
/// <remarks>
/// <para>
/// Classification is a <em>market fact</em> declared by the issuer — not a tax
/// computation. Tax treatment tables (gross-up factors, dividend tax credit
/// eligibility, withholding rates) live in <c>Boutquin.TaxEngine</c> and key off
/// these categories.
/// </para>
/// <para>
/// Adapter classification policy. Adapters SHOULD select the most-specific applicable
/// category (typically <see cref="Foreign"/> for non-US issuers, or the investor base
/// currency's jurisdictional default) and log the inference. Adapters MUST NOT silently
/// emit <see cref="Unclassified"/> when any more-specific category would apply —
/// consumers treat <see cref="Unclassified"/> as a last-resort fallback reserved for
/// data points where every other category has been ruled out.
/// </para>
/// <para>
/// Consumer exhaustiveness. Switch expressions over <see cref="DividendType"/> MUST
/// handle <see cref="Unclassified"/> explicitly (typically by throwing or logging), and
/// tests that assert exhaustiveness should use <see cref="Enum.GetValues"/> so new
/// members force test failures rather than silent fall-through.
/// </para>
/// <para>
/// Relocated from <c>Boutquin.Trading.Domain.Enums.DividendType</c>. Member
/// names and ordinals preserved verbatim.
/// </para>
/// </remarks>
public enum DividendType
{
    /// <summary>Canadian eligible dividend (38% gross-up, enhanced DTC).</summary>
    CanadianEligible,

    /// <summary>Canadian non-eligible dividend (15% gross-up, basic DTC).</summary>
    CanadianNonEligible,

    /// <summary>US qualified dividend (capital gains rate).</summary>
    UsQualified,

    /// <summary>US ordinary dividend (ordinary income rate).</summary>
    UsOrdinary,

    /// <summary>Foreign dividend (ordinary income, may have foreign tax credit).</summary>
    Foreign,

    /// <summary>Return of capital (reduces cost basis, not income).</summary>
    ReturnOfCapital,

    /// <summary>Capital gain distribution (from fund internals).</summary>
    CapitalGainDistribution,

    /// <summary>
    /// Last-resort fallback for issuers whose jurisdictional category cannot be determined
    /// even after applying the adapter classification policy. Consumers should treat this
    /// as a signal to surface a data-quality warning rather than attempting tax computation.
    /// </summary>
    Unclassified,
}
