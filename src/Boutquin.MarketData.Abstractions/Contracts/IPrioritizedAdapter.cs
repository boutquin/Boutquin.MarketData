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

namespace Boutquin.MarketData.Abstractions.Contracts;

/// <summary>
/// Marker interface for data source adapters that participate in priority-based fallback selection.
/// </summary>
/// <remarks>
/// <para>
/// The fallback orchestrator sorts adapters by descending <see cref="Priority"/> and tries them
/// in order until one produces a usable result. Higher values indicate higher priority
/// (e.g., 100 = primary source, 50 = secondary, 10 = last-resort).
/// </para>
/// <para>
/// Adapters that do not implement this interface are treated as unranked (priority 0)
/// and are tried after all prioritized adapters.
/// </para>
/// </remarks>
public interface IPrioritizedAdapter
{
    /// <summary>
    /// The priority of this adapter for fallback ordering. Higher values are tried first.
    /// </summary>
    int Priority { get; }
}
