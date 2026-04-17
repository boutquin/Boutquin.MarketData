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
/// Canonical per-country reference data: the continent the country belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Placing the mapping here — rather than as a property on <see cref="CountryCode"/>
/// itself — keeps the enum a pure identifier and the mapping swappable if
/// geographic classifications evolve (for example, recognising a new country or
/// reclassifying transcontinental jurisdictions).
/// </para>
/// <para>
/// <strong>Russia.</strong> Russia is mapped to <see cref="ContinentCode.EU"/>
/// following the UN M49 geoscheme (code 151 = Europe), which places Russia in
/// the Europe group for statistical purposes even though the country straddles
/// Europe and Asia geographically. Moscow Exchange — the main market — sits in
/// European Russia.
/// </para>
/// </remarks>
public static class CountryMetadata
{
    private static readonly IReadOnlyDictionary<CountryCode, ContinentCode> s_continents =
        new Dictionary<CountryCode, ContinentCode>
        {
            [CountryCode.CA] = ContinentCode.NA,
            [CountryCode.CN] = ContinentCode.AS,
            [CountryCode.FR] = ContinentCode.EU,
            [CountryCode.DE] = ContinentCode.EU,
            [CountryCode.HK] = ContinentCode.AS,
            [CountryCode.IN] = ContinentCode.AS,
            [CountryCode.JP] = ContinentCode.AS,
            [CountryCode.RU] = ContinentCode.EU,
            [CountryCode.KR] = ContinentCode.AS,
            [CountryCode.GB] = ContinentCode.EU,
            [CountryCode.US] = ContinentCode.NA,
        };

    /// <summary>
    /// Returns the continent for the given country. Exhaustive over <see cref="CountryCode"/>.
    /// </summary>
    /// <param name="country">The country whose continent to resolve.</param>
    /// <returns>The <see cref="ContinentCode"/> the country belongs to.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="country"/> is not a declared <see cref="CountryCode"/>
    /// member (for example, a <c>(CountryCode)(-1)</c> cast) — the exhaustiveness
    /// test covers every declared member, so this never fires for legitimate input.
    /// </exception>
    public static ContinentCode GetContinent(CountryCode country)
    {
        if (s_continents.TryGetValue(country, out var continent))
        {
            return continent;
        }

        throw new ArgumentOutOfRangeException(
            nameof(country),
            country,
            $"No continent mapping is registered for CountryCode.{country}. " +
            "Add the mapping in CountryMetadata when introducing the enum member.");
    }
}
