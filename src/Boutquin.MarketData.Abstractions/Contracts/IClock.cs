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
/// Abstraction over system time, enabling deterministic tests and controlled time progression.
/// </summary>
/// <remarks>
/// Adapters, caches, and staleness evaluators should depend on <see cref="IClock"/> rather
/// than calling <see cref="DateTimeOffset.UtcNow"/> directly. In tests, inject a fake clock
/// to produce repeatable results; in production, register <c>SystemClock</c> from the
/// DependencyInjection package.
/// </remarks>
public interface IClock
{
    /// <summary>
    /// The current UTC date and time.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
