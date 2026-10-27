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

namespace Boutquin.MarketData.Abstractions.Observability;

/// <summary>
/// Sizes persisted point-lookup Bloom filters in row and columnar time-series segments.
/// </summary>
/// <remarks>
/// Settings apply to newly written and compacted segments. Existing filters reopen from their
/// persisted state. Range scans and range tombstones do not use point Bloom membership.
/// The immutable options can be shared between builders.
/// </remarks>
public sealed record TimeSeriesBloomFilterOptions
{
    /// <summary>
    /// Whether new segments include a Bloom filter. Disabling writes does not disable persisted filters on reads.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Expected number of distinct keys per segment, sizing the bit array. Defaults to 131,072.
    /// </summary>
    public int ExpectedItems { get; init; } = 131_072;

    /// <summary>
    /// Target false-positive probability in the open interval (0, 1). Defaults to 0.01 (1%).
    /// </summary>
    public double FalsePositiveRate { get; init; } = 0.01;
}
