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
/// A single node on a yield curve: a tenor label paired with an annualized rate in decimal form.
/// </summary>
/// <param name="Tenor">The maturity label (e.g., "3M", "1Y", "10Y", "30Y").</param>
/// <param name="Rate">The annualized rate in decimal form (e.g., 0.045 for 4.5%).</param>
public sealed record YieldCurveQuote(string Tenor, decimal Rate);
