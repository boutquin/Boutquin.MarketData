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
/// Canonical per-exchange reference data: trading-session timezone and country
/// of incorporation / listing.
/// </summary>
/// <remarks>
/// <para>
/// Looking up session timezone or country belongs at the MarketData layer —
/// every consumer localising <see cref="Records.Bar"/> or <see cref="Records.FxRate"/>
/// timestamps, or aggregating by jurisdiction, hits these two questions. Placing
/// the tables on the enum itself would require a record-struct mirror kept in
/// sync; a static lookup keeps the enum the single source of truth for the
/// member list and colocates the mapping in one place.
/// </para>
/// <para>
/// <strong>Lifecycle contract.</strong> The lookup tables are type-initialised
/// (constructed once on first access via the CLR's type initialiser) and
/// exposed through a closed surface — no <c>Register</c> / <c>Set</c> extension
/// points. If a consumer needs a mapping that isn't present, the fix is to add
/// an <see cref="ExchangeCode"/> member plus its mapping in the same commit,
/// not to register a runtime override.
/// </para>
/// <para>
/// <strong>Daylight Saving.</strong> The returned <see cref="TimeZoneCode"/> is
/// the <em>standard-time</em> designation for the exchange (for example, XNYS
/// returns <see cref="TimeZoneCode.EST"/> year-round, not <c>EDT</c> in summer).
/// Consumers that need DST-aware session hours must resolve the offset through
/// <c>TimeZoneInfo</c> or an equivalent IANA-aware library — the enum is a
/// classification, not a clock.
/// </para>
/// </remarks>
public static class ExchangeMetadata
{
    private static readonly IReadOnlyDictionary<ExchangeCode, TimeZoneCode> s_timeZones =
        new Dictionary<ExchangeCode, TimeZoneCode>
        {
            [ExchangeCode.XNYS] = TimeZoneCode.EST,
            [ExchangeCode.XNAS] = TimeZoneCode.EST,
            [ExchangeCode.XTSE] = TimeZoneCode.JST,
            [ExchangeCode.XSHG] = TimeZoneCode.CST,
            [ExchangeCode.XHKG] = TimeZoneCode.HKT,
            [ExchangeCode.XPAR] = TimeZoneCode.CET,
            [ExchangeCode.XLON] = TimeZoneCode.GMT,
            [ExchangeCode.XETR] = TimeZoneCode.CET,
            [ExchangeCode.XMOS] = TimeZoneCode.MSK,
            [ExchangeCode.XTOR] = TimeZoneCode.EST,
            [ExchangeCode.NEOE] = TimeZoneCode.EST,
        };

    private static readonly IReadOnlyDictionary<ExchangeCode, CountryCode> s_countries =
        new Dictionary<ExchangeCode, CountryCode>
        {
            [ExchangeCode.XNYS] = CountryCode.US,
            [ExchangeCode.XNAS] = CountryCode.US,
            [ExchangeCode.XTSE] = CountryCode.JP,
            [ExchangeCode.XSHG] = CountryCode.CN,
            [ExchangeCode.XHKG] = CountryCode.HK,
            [ExchangeCode.XPAR] = CountryCode.FR,
            [ExchangeCode.XLON] = CountryCode.GB,
            [ExchangeCode.XETR] = CountryCode.DE,
            [ExchangeCode.XMOS] = CountryCode.RU,
            [ExchangeCode.XTOR] = CountryCode.CA,
            [ExchangeCode.NEOE] = CountryCode.CA,
        };

    /// <summary>
    /// Returns the canonical trading-session timezone for the given exchange.
    /// </summary>
    /// <param name="exchange">The exchange whose session timezone to resolve.</param>
    /// <returns>The standard-time <see cref="TimeZoneCode"/> for the exchange.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="exchange"/> is not a declared <see cref="ExchangeCode"/>
    /// member (for example, a <c>(ExchangeCode)(-1)</c> cast) — the exhaustiveness
    /// test covers every declared member, so this never fires for legitimate input.
    /// </exception>
    public static TimeZoneCode GetTimeZone(ExchangeCode exchange)
    {
        if (s_timeZones.TryGetValue(exchange, out var tz))
        {
            return tz;
        }

        throw new ArgumentOutOfRangeException(
            nameof(exchange),
            exchange,
            $"No timezone mapping is registered for ExchangeCode.{exchange}. " +
            "Add the mapping in ExchangeMetadata when introducing the enum member.");
    }

    /// <summary>
    /// Returns the canonical country of incorporation or listing for the given exchange.
    /// </summary>
    /// <param name="exchange">The exchange whose country to resolve.</param>
    /// <returns>The <see cref="CountryCode"/> of the exchange's home jurisdiction.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="exchange"/> is not a declared <see cref="ExchangeCode"/> member.
    /// </exception>
    public static CountryCode GetCountry(ExchangeCode exchange)
    {
        if (s_countries.TryGetValue(exchange, out var country))
        {
            return country;
        }

        throw new ArgumentOutOfRangeException(
            nameof(exchange),
            exchange,
            $"No country mapping is registered for ExchangeCode.{exchange}. " +
            "Add the mapping in ExchangeMetadata when introducing the enum member.");
    }
}
