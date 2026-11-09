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

using Boutquin.MarketData.Abstractions.Requests;

namespace Boutquin.MarketData.Abstractions.Exceptions;

/// <summary>Refuses a price request for which no adapter declares the requested basis.</summary>
public sealed class PriceBasisNotSupportedException : MarketDataException
{
    /// <summary>Records a refused basis and a defensive, distinct, ordinally ordered list of declarations.</summary>
    /// <param name="requested">The requested price basis.</param>
    /// <param name="declared">Available adapter bases; the caller retains ownership of the collection.</param>
    /// <exception cref="ArgumentNullException">The declaration list is null.</exception>
    public PriceBasisNotSupportedException(PriceBasis requested, IReadOnlyList<PriceBasis> declared)
        : base(BuildMessage(requested, declared))
    {
        Requested = requested;
        Declared = Array.AsReadOnly(declared.Distinct().OrderBy(basis => basis.Token, StringComparer.Ordinal).ToArray());
    }
    /// <summary>The basis that could not be served.</summary>
    public PriceBasis Requested { get; }
    /// <summary>The distinct declared bases sorted by their ordinal token.</summary>
    public IReadOnlyList<PriceBasis> Declared { get; }

    private static string BuildMessage(PriceBasis requested, IReadOnlyList<PriceBasis> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);
        var tokens = declared.Select(basis => basis.Token).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return $"No registered price adapter declares price basis '{requested.Token}'. Declared: {(tokens.Length == 0 ? "none" : string.Join(", ", tokens))}. Price adapters must implement IPriceBasisAdapter; see docs/migration-3.0.md.";
    }
}
