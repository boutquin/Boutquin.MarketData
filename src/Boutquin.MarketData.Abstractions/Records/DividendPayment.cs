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

using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.Storage.Domain.Attributes;
using Boutquin.Storage.Domain.Interfaces.TimeSeries;

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// A single cash dividend payment observation, typically sourced from exchange or provider APIs.
/// </summary>
/// <param name="ExDate">The ex-dividend date — the first date on which buyers do not receive the dividend.</param>
/// <param name="Amount">The per-share cash dividend amount.</param>
/// <param name="Currency">The ISO 4217 currency of the payment.</param>
/// <param name="Type">
/// The issuer-declared jurisdictional category of the dividend. Adapters that
/// cannot derive a precise classification from the upstream source select the
/// closest applicable <see cref="DividendType"/> member and log the inference.
/// The classification policy — which member to choose, and the one case in which
/// an amount may legitimately be left <see cref="DividendType.Unclassified"/> —
/// is stated on <see cref="DividendType"/> itself.
/// </param>
/// <remarks>
/// <para>Used by both Trading (adjusted-price computation, total-return calculation) and
/// OptionPricing (discrete dividend modeling in tree pricers), and by TaxEngine
/// (income classification and withholding computation via <paramref name="Type"/>).
/// Adapters emit one record per ex-date; consumers aggregate into schedules as needed.</para>
/// <para>Declared <c>partial</c> + <see cref="IHasDate"/> so the time-series store can order and
/// partition these records by date. Because the sort-defining date on this record is named
/// <see cref="ExDate"/> (not <c>Date</c>), <see cref="IHasDate.Date"/> is implemented via
/// explicit interface implementation that returns <see cref="ExDate"/>.</para>
/// </remarks>
[StorageSerializable]
public partial record struct DividendPayment(
    [property: Tag(1)] DateOnly ExDate,
    [property: Tag(2)] decimal Amount,
    [property: Tag(3)] Currency Currency,
    [property: Tag(4)] DividendType Type) : IHasDate
{
    /// <inheritdoc />
    readonly DateOnly IHasDate.Date => ExDate;
}
