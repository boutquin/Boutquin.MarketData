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

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// An OHLCAV price bar representing a single trading period for an equity, ETF, or similar instrument.
/// </summary>
/// <param name="Date">The trading date of the bar.</param>
/// <param name="Open">Opening price.</param>
/// <param name="High">Highest price during the period.</param>
/// <param name="Low">Lowest price during the period.</param>
/// <param name="Close">Closing (unadjusted) price.</param>
/// <param name="AdjustedClose">Split- and dividend-adjusted closing price.</param>
/// <param name="Volume">Number of shares or contracts traded.</param>
public sealed record Bar(
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal AdjustedClose,
    long Volume);
