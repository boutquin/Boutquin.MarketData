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
/// A multi-factor return observation for a single date, used for academic factor models
/// (e.g., Fama-French 3-factor, 5-factor, momentum).
/// </summary>
/// <param name="Date">The observation date.</param>
/// <param name="Factors">Factor returns keyed by factor name (e.g., "Mkt-RF", "SMB", "HML"), in decimal form.</param>
public sealed record FactorObservation(
    DateOnly Date,
    IReadOnlyDictionary<string, decimal> Factors);
