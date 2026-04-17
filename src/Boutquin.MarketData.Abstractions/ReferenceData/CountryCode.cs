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
/// Represents the ISO 3166-1:2020 alpha-2 country codes relevant to MarketData
/// consumers (instrument domicile, exchange jurisdiction, issuer country).
/// </summary>
/// <remarks>
/// <para>
/// Relocated from <c>Boutquin.Trading.Domain.Enums.CountryCode</c> to the shared
/// MarketData reference-data namespace. Member names and numeric ordinals are
/// preserved verbatim so existing Trading code that round-trips through an
/// <see cref="int"/> cast continues to produce identical results.
/// </para>
/// <para>
/// The catalogue is an <em>open set</em> curated to the countries MarketData
/// cares about today — new members append at the end to preserve ordinals.
/// </para>
/// </remarks>
public enum CountryCode
{
    /// <summary>
    /// Canada.
    /// </summary>
    [Description("Canada")]
    CA,

    /// <summary>
    /// China.
    /// </summary>
    [Description("China")]
    CN,

    /// <summary>
    /// France.
    /// </summary>
    [Description("France")]
    FR,

    /// <summary>
    /// Germany.
    /// </summary>
    [Description("Germany")]
    DE,

    /// <summary>
    /// Hong Kong.
    /// </summary>
    [Description("Hong Kong")]
    HK,

    /// <summary>
    /// India.
    /// </summary>
    [Description("India")]
    IN,

    /// <summary>
    /// Japan.
    /// </summary>
    [Description("Japan")]
    JP,

    /// <summary>
    /// Russia.
    /// </summary>
    [Description("Russia")]
    RU,

    /// <summary>
    /// South Korea.
    /// </summary>
    [Description("South Korea")]
    KR,

    /// <summary>
    /// United Kingdom.
    /// </summary>
    [Description("United Kingdom")]
    GB,

    /// <summary>
    /// United States.
    /// </summary>
    [Description("United States")]
    US
}
