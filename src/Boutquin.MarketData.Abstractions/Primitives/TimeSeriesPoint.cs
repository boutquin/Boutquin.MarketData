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

namespace Boutquin.MarketData.Abstractions.Primitives;

/// <summary>
/// A single dated observation pairing a calendar date with a typed value.
/// </summary>
/// <typeparam name="T">The observation value type (e.g., <c>decimal</c> for a price,
/// a <c>Bar</c> record for OHLCV data).</typeparam>
/// <param name="Date">The calendar date of the observation.</param>
/// <param name="Value">The observed value on that date.</param>
/// <remarks>
/// Domain repos define the <typeparamref name="T"/> — the kernel provides the container.
/// For example, Trading uses <c>TimeSeriesPoint&lt;Bar&gt;</c>, Market uses
/// <c>TimeSeriesPoint&lt;decimal&gt;</c> for yield observations.
/// </remarks>
public sealed record TimeSeriesPoint<T>(DateOnly Date, T Value);
