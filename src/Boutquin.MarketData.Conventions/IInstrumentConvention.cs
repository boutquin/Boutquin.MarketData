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

namespace Boutquin.MarketData.Conventions;

/// <summary>
/// Shared contract implemented by every instrument convention record (OIS, FRA,
/// swap leg, futures, settlement) — guarantees a <see cref="Code"/> lookup key
/// so an <see cref="InstrumentConventionRegistry"/> can store heterogenous
/// conventions in a single keyed dictionary.
/// </summary>
public interface IInstrumentConvention
{
    /// <summary>
    /// Unique convention code used for lookup in convention registries.
    /// </summary>
    string Code { get; }
}
