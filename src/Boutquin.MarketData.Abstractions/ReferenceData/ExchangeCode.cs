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
/// Represents the ISO 10383 market identifier codes for the major exchanges.
/// </summary>
/// <remarks>
/// <para>
/// Member names are ISO 10383 MIC tokens. Numeric ordinals are stable — inserting new
/// members in the middle would shift every subsequent serialized value, so additions
/// always append at the end.
/// </para>
/// <para>
/// <b>2026-04-20 rename:</b> the enum previously carried the non-ISO labels
/// <c>XTSE</c> for Tokyo and <c>XTOR</c> for Toronto, which inverted the ISO 10383
/// assignments (`XTSE` is Toronto, `XTKS` is Tokyo). The rename is a breaking change
/// to the string form of the enum (<c>ToString()</c>, JSON string-serialized values)
/// but preserves every numeric ordinal, so integer-serialized data is unaffected.
/// Downstream: anything keying on the string "XTSE" now means Toronto, not Tokyo; any
/// string persisted as "XTOR" is no longer a recognized member. No portfolio or
/// benchmark in the ETFWealthIQ universe referenced Tokyo-listed instruments at the
/// time of the rename, so no persisted data was migrated.
/// </para>
/// </remarks>
public enum ExchangeCode
{
    /// <summary>
    /// New York Stock Exchange.
    /// </summary>
    [Description("New York Stock Exchange")]
    XNYS,

    /// <summary>
    /// NASDAQ Stock Market.
    /// </summary>
    [Description("NASDAQ Stock Market")]
    XNAS,

    /// <summary>
    /// Tokyo Stock Exchange (operating MIC within Japan Exchange Group).
    /// </summary>
    /// <remarks>
    /// Ordinal pinned to slot 2 to preserve backward compatibility with numerically
    /// serialized data that was written before the 2026-04-20 rename (was <c>XTSE</c>).
    /// </remarks>
    [Description("Tokyo Stock Exchange")]
    XTKS,

    /// <summary>
    /// Shanghai Stock Exchange.
    /// </summary>
    [Description("Shanghai Stock Exchange")]
    XSHG,

    /// <summary>
    /// Hong Kong Stock Exchange.
    /// </summary>
    [Description("Hong Kong Stock Exchange")]
    XHKG,

    /// <summary>
    /// Euronext Paris.
    /// </summary>
    [Description("Euronext Paris")]
    XPAR,

    /// <summary>
    /// London Stock Exchange.
    /// </summary>
    [Description("London Stock Exchange")]
    XLON,

    /// <summary>
    /// Deutsche Boerse XETRA.
    /// </summary>
    [Description("Deutsche Boerse XETRA")]
    XETR,

    /// <summary>
    /// Central Counterparty Clearing Center MFB, a Russian clearing organization.
    /// </summary>
    /// <remarks>
    /// ISO 10383 assigns this MIC to the clearing organization. Use <see cref="MISX"/>
    /// for Moscow Exchange; the existing numeric value of XMOS remains unchanged.
    /// </remarks>
    [Description("Central Counterparty Clearing Center MFB")]
    XMOS,

    /// <summary>
    /// Toronto Stock Exchange (TMX Group).
    /// </summary>
    /// <remarks>
    /// Ordinal pinned to slot 9 to preserve backward compatibility with numerically
    /// serialized data that was written before the 2026-04-20 rename (was <c>XTOR</c>).
    /// </remarks>
    [Description("Toronto Stock Exchange")]
    XTSE,

    /// <summary>
    /// Cboe Canada NEO-L market, formerly Aequitas NEO Exchange, Toronto.
    /// </summary>
    [Description("Cboe Canada NEO-L")]
    NEOE,

    /// <summary>
    /// Moscow Exchange, all markets (ISO 10383 operating MIC MISX).
    /// </summary>
    [Description("Moscow Exchange")]
    MISX
}
