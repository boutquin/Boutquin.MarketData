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

using System.ComponentModel;

namespace Boutquin.MarketData.Abstractions.ReferenceData;

/// <summary>
/// Enumerates the asset classes used in the schema.
/// </summary>
/// <remarks>
/// Relocated from <c>Boutquin.Trading.Domain.Enums.AssetClassCode</c>. Member names
/// are preserved verbatim so code that persists or emits the textual value
/// continues to round-trip without translation.
/// </remarks>
public enum AssetClassCode
{
    /// <summary>
    /// Cash or Cash Equivalents.
    /// </summary>
    [Description("Cash or Cash Equivalents")]
    CashAndCashEquivalents,

    /// <summary>
    /// Fixed Income Securities.
    /// </summary>
    [Description("Fixed Income Securities")]
    FixedIncome,

    /// <summary>
    /// Equity Securities.
    /// </summary>
    [Description("Equity Securities")]
    Equities,

    /// <summary>
    /// Real Estate.
    /// </summary>
    [Description("Real Estate")]
    RealEstate,

    /// <summary>
    /// Commodities.
    /// </summary>
    [Description("Commodities")]
    Commodities,

    /// <summary>
    /// Alternative Investments.
    /// </summary>
    [Description("Alternative Investments")]
    Alternatives,

    /// <summary>
    /// Cryptocurrencies.
    /// </summary>
    [Description("Crypto-Currencies")]
    CryptoCurrencies,

    /// <summary>
    /// Other.
    /// </summary>
    [Description("Other")]
    Other
}
