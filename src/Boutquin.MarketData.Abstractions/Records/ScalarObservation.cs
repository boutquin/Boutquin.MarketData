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
/// A single-value time series observation, used for economic indicators, benchmark rates,
/// and other scalar data (e.g., FRED GDP, 10Y Treasury yield, SOFR fixing).
/// </summary>
/// <param name="Date">The observation date.</param>
/// <param name="Value">The observed value, already normalized (e.g., decimal form, not percent).</param>
/// <param name="Units">The unit of measurement (e.g., "decimal", "index", "billions-of-dollars").</param>
public sealed record ScalarObservation(
    DateOnly Date,
    decimal Value,
    string Units);
