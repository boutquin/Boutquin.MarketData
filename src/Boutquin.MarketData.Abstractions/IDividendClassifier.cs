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

using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions;

/// <summary>
/// Classifies a raw dividend into a <see cref="DividendType"/> based on asset
/// identity, the asset's issuing currency, and the investor's base currency.
/// </summary>
/// <remarks>
/// <para>
/// Classification is a data-layer concern — it produces a market fact that
/// downstream consumers (tax engines, portfolio-income aggregators, reporting
/// pipelines) read without re-deriving. The classifier takes the investor's
/// base currency because jurisdiction-relative categories (US qualified vs US
/// ordinary vs Foreign) depend on the investor's tax residency, not just the
/// issuer's.
/// </para>
/// <para>
/// <strong>Unknown-input contract.</strong> Implementations MUST be defined on
/// every well-formed triple <c>(symbol, assetCurrency, investorBaseCurrency)</c>.
/// Specifically:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <see cref="Classify"/> never throws and never returns
///     <c>default(DividendType)</c> to signal "unclassifiable". Every call
///     returns an explicit <see cref="DividendType"/> member.
///     </description>
///   </item>
///   <item>
///     <description>
///     When the classifier cannot determine a precise type from its metadata
///     tables, it returns the broadest applicable category:
///     <see cref="DividendType.Foreign"/> when the asset currency differs from
///     the investor's base currency, and the jurisdictional default of the
///     investor's base currency (for example, <see cref="DividendType.UsOrdinary"/>
///     for USD or <see cref="DividendType.CanadianNonEligible"/> for CAD) when
///     they match. This "broad fallback" keeps downstream
///     <see cref="Records.DividendPayment.Type"/> well-defined without
///     re-introducing an <c>Unknown</c> sentinel on <see cref="DividendType"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>default(Symbol)</c> is not a supported input —
///     <see cref="ReferenceData.Symbol"/>'s constructor rejects null/whitespace
///     tickers, so a <c>default(Symbol)</c> can only arise from explicit misuse.
///     Implementations MAY throw <see cref="ArgumentException"/> in that case.
///     </description>
///   </item>
/// </list>
/// <para>
/// Relocated from <c>Boutquin.Trading.Domain.Interfaces.IDividendClassifier</c>.
/// The first parameter renamed from <c>Asset asset</c> to
/// <see cref="ReferenceData.Symbol"/> <c>symbol</c> — same underlying type
/// after the Tier 1 relocation, role-renamed for the data layer.
/// </para>
/// </remarks>
public interface IDividendClassifier
{
    /// <summary>
    /// Classifies a raw dividend into a <see cref="DividendType"/>.
    /// </summary>
    /// <param name="symbol">The symbol whose dividend is being classified.</param>
    /// <param name="assetCurrency">The ISO 4217 currency the dividend is paid in.</param>
    /// <param name="investorBaseCurrency">The ISO 4217 base currency of the portfolio receiving the dividend.</param>
    /// <returns>
    /// An explicitly selected <see cref="DividendType"/> member, including a legitimate
    /// zero-valued member when applicable. Do not use <c>default(DividendType)</c> as an
    /// unclassified sentinel. See the "Unknown-input contract" in the interface remarks
    /// for the broad-fallback rules.
    /// </returns>
    DividendType Classify(Symbol symbol, Currency assetCurrency, Currency investorBaseCurrency);
}
