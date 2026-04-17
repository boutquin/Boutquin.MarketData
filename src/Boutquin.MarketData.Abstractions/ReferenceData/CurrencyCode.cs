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
/// Represents the ISO 4217 currency codes.
/// </summary>
/// <remarks>
/// <para>
/// Relocated from <c>Boutquin.Trading.Domain.Enums.CurrencyCode</c> to the shared
/// MarketData reference-data namespace. Member names and numeric (ISO 4217) values
/// are preserved verbatim so existing Trading, TaxEngine, and Analytics code that
/// round-trips through an <see cref="int"/> cast continues to produce identical
/// results.
/// </para>
/// <para>
/// The enum is a <em>closed set</em> maintained by ISO — new members are added
/// only when ISO issues a revision, so compile-time exhaustiveness on switch
/// expressions is a correct and desirable property.
/// </para>
/// </remarks>
public enum CurrencyCode
{
    /// <summary>
    /// United States dollar.
    /// </summary>
    [Description("United States dollar")]
    USD = 840,

    /// <summary>
    /// Canadian dollar.
    /// </summary>
    [Description("Canadian dollar")]
    CAD = 124,

    /// <summary>
    /// Mexican peso.
    /// </summary>
    [Description("Mexican peso")]
    MXN = 484,

    /// <summary>
    /// British pound.
    /// </summary>
    [Description("British pound")]
    GBP = 826,

    /// <summary>
    /// Euro.
    /// </summary>
    [Description("Euro")]
    EUR = 978,

    /// <summary>
    /// Japanese yen.
    /// </summary>
    [Description("Japanese yen")]
    JPY = 392,

    /// <summary>
    /// Chinese yuan.
    /// </summary>
    [Description("Chinese yuan")]
    CNY = 156,

    /// <summary>
    /// Indian rupee.
    /// </summary>
    [Description("Indian rupee")]
    INR = 356,

    /// <summary>
    /// Australian dollar.
    /// </summary>
    [Description("Australian dollar")]
    AUD = 36,

    /// <summary>
    /// Brazilian real.
    /// </summary>
    [Description("Brazilian real")]
    BRL = 986,

    /// <summary>
    /// Russian ruble.
    /// </summary>
    [Description("Russian ruble")]
    RUB = 643,

    /// <summary>
    /// South Korean won.
    /// </summary>
    [Description("South Korean won")]
    KRW = 410,

    /// <summary>
    /// Hong Kong dollar.
    /// </summary>
    [Description("Hong Kong dollar")]
    HKD = 344,

    /// <summary>
    /// Swiss franc.
    /// </summary>
    /// <remarks>
    /// Added in the reference-data consolidation spec to support CHF SARON benchmark
    /// metadata (Hull 11e Ch 4 p.99–100). The Trading-repo shim MUST mirror this addition
    /// in the same release cycle — see spec §6 "drift is not a supported state".
    /// </remarks>
    [Description("Swiss franc")]
    CHF = 756
}
