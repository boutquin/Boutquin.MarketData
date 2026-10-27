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

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// A corporate action event affecting a security's price history, share count, or ownership structure.
/// </summary>
/// <param name="EffectiveDate">The date the action takes effect (ex-date for dividends, effective date for splits).</param>
/// <param name="Symbol">The ticker symbol of the affected security.</param>
/// <param name="ActionType">The type of corporate action.</param>
/// <param name="Factor">
/// The multiplicative adjustment factor. For splits, this is the ratio (e.g., 4.0 for 4:1).
/// For spin-offs, the distribution ratio. <see langword="null"/> when not applicable
/// (e.g., cash dividends use <paramref name="Amount"/> instead).
/// </param>
/// <param name="Amount">
/// The per-share cash amount. Used for <see cref="CorporateActionType.CashDividend"/>.
/// <see langword="null"/> for non-cash actions.
/// </param>
/// <param name="Currency">
/// The ISO 4217 currency of cash amounts, or <see langword="null"/> for non-cash actions.
/// </param>
/// <param name="Description">Optional free-text description from the data source (e.g., "4-for-1 stock split").</param>
/// <remarks>
/// This record generalizes dividends, splits, spin-offs, and rights issues into a single
/// canonical shape. Adapters (Tiingo, TwelveData) parse provider-specific formats into this
/// record. Trading uses it for adjusted-price computation; OptionPricing for discrete
/// dividend modeling.
/// </remarks>
public sealed record CorporateAction(
    DateOnly EffectiveDate,
    Symbol Symbol,
    CorporateActionType ActionType,
    decimal? Factor,
    decimal? Amount,
    Currency? Currency,
    string? Description);
