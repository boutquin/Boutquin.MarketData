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

using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// A futures contract settlement observation, typically from an exchange end-of-day report.
/// </summary>
/// <param name="Date">The settlement date.</param>
/// <param name="ContractCode">The product code (e.g., <c>new FuturesProductCode("SR3")</c> for 3-month SOFR futures).</param>
/// <param name="ContractMonth">The delivery month (canonical <c>YYYY-MM</c>).</param>
/// <param name="SettlePrice">The daily settlement price.</param>
/// <param name="ImpliedRate">The implied interest rate derived from the settlement price (100 - price), or <see langword="null"/> if not applicable.</param>
public sealed record FuturesSettlement(
    DateOnly Date,
    FuturesProductCode ContractCode,
    ContractMonth ContractMonth,
    decimal SettlePrice,
    decimal? ImpliedRate);
