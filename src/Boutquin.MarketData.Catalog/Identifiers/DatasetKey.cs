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

namespace Boutquin.MarketData.Catalog.Identifiers;

/// <summary>
/// Strongly typed identifier for a dataset, preventing stringly-typed key misuse.
/// </summary>
/// <param name="Value">The underlying string value of the dataset key.</param>
/// <remarks>
/// <para>
/// Use this type instead of bare strings when passing dataset identifiers between
/// pipeline components. The value is provider-scoped (e.g., <c>"tiingo:AAPL"</c>,
/// <c>"fred:DGS10"</c>) and should be treated as opaque by infrastructure code.
/// </para>
/// <para>
/// Explicit construction (<c>new DatasetKey("…")</c>) validates that the value is neither
/// <see langword="null"/> nor whitespace. <c>default(DatasetKey)</c> is still reachable as
/// a zero-value via struct semantics and has <c>Value = null</c>; consumers MUST check for
/// this when they cannot trust the origin of a <see cref="DatasetKey"/> instance.
/// </para>
/// </remarks>
public readonly record struct DatasetKey(string Value)
{
    /// <summary>
    /// Gets the underlying string value. Always non-null and non-whitespace for instances
    /// created via the primary constructor.
    /// </summary>
    public string Value { get; init; } = ValidateValue(Value);

    private static string ValidateValue(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }
}
