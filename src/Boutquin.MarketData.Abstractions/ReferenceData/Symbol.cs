// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

using System.Text.Json.Serialization;

namespace Boutquin.MarketData.Abstractions.ReferenceData;

/// <summary>
/// Strongly-typed identifier for a financial instrument ticker symbol.
/// </summary>
/// <remarks>
/// <para>
/// Relocated from <c>Boutquin.Trading.Domain.ValueObjects.Asset</c> and renamed —
/// <em>Asset</em> is role-centric ("the thing you invest in"), whereas at the data
/// layer the same value is an identifier for lookup ("the thing you look up"). The
/// rename is intentional so that <see cref="Symbol"/> reads naturally on records
/// like <see cref="Records.InstrumentMetadata"/>, on request inputs, and as a
/// dictionary key (<c>Dictionary&lt;Symbol, Bar&gt;</c>).
/// </para>
/// <para>
/// <see cref="Symbol"/> is a <c>readonly</c> record struct: immutable, value-typed,
/// with structural equality. Storing it inline on request objects avoids a heap
/// allocation for every ticker a pipeline handles. The constructor rejects null or
/// whitespace tickers so <c>default(Symbol)</c> is the only way to obtain an
/// empty-ticker instance — avoid it; construct explicitly.
/// </para>
/// <para>
/// <see cref="CompareTo"/> uses <see cref="StringComparison.Ordinal"/> so that
/// <see cref="SortedDictionary{TKey, TValue}"/>-keyed by <see cref="Symbol"/>
/// produces deterministic, culture-invariant ordering. Case sensitivity is
/// preserved by design: "AAPL" and "aapl" are distinct symbols because some
/// markets (TSX) distinguish ticker casing.
/// </para>
/// </remarks>
public readonly record struct Symbol : IComparable<Symbol>
{
    /// <summary>
    /// The ticker text (e.g., "AAPL", "MSFT", "RY.TO").
    /// </summary>
    public string Ticker { get; }

    /// <summary>
    /// Initializes a new <see cref="Symbol"/> with the given ticker text.
    /// </summary>
    /// <param name="ticker">
    /// The ticker text. Must be non-null and contain at least one non-whitespace
    /// character.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="ticker"/> is <see langword="null"/>, empty, or
    /// consists only of whitespace characters.
    /// </exception>
    /// <remarks>
    /// Annotated <see cref="JsonConstructorAttribute"/> so <see cref="System.Text.Json.JsonSerializer"/>
    /// invokes this constructor (populating <see cref="Ticker"/>) on round-trip rather than
    /// defaulting to the implicit parameterless struct constructor, which would leave
    /// <see cref="Ticker"/> <see langword="null"/> because the property is get-only.
    /// </remarks>
    [JsonConstructor]
    public Symbol(string ticker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        Ticker = ticker;
    }

    /// <summary>
    /// Compares this symbol with another using ordinal string ordering on
    /// <see cref="Ticker"/>.
    /// </summary>
    /// <param name="other">The symbol to compare against.</param>
    /// <returns>
    /// A negative value if this symbol precedes <paramref name="other"/> in
    /// ordinal order; zero if equal; positive if this symbol follows.
    /// </returns>
    public int CompareTo(Symbol other) =>
        string.CompareOrdinal(Ticker, other.Ticker);

    /// <summary>
    /// Returns the underlying ticker text.
    /// </summary>
    /// <returns>The value of <see cref="Ticker"/>.</returns>
    public override string ToString() => Ticker ?? string.Empty;
}
