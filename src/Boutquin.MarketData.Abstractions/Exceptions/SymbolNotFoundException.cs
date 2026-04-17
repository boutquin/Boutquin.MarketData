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

namespace Boutquin.MarketData.Abstractions.Exceptions;

/// <summary>
/// Thrown when a symbol-resolution or symbol-lookup step cannot locate the
/// requested ticker in the underlying reference-data source.
/// </summary>
/// <remarks>
/// Renamed on relocation from <c>Boutquin.Trading.Domain.Exceptions.SymbolReaderException</c> —
/// the new name drops the implementation-specific "Reader" suffix so the type
/// can be thrown by any MarketData code path that fails to resolve a symbol,
/// not just the symbol-reader pipeline.
/// </remarks>
public sealed class SymbolNotFoundException : MarketDataException
{
    /// <summary>
    /// Initializes a new <see cref="SymbolNotFoundException"/> with no message.
    /// </summary>
    public SymbolNotFoundException()
    {
    }

    /// <summary>
    /// Initializes a new <see cref="SymbolNotFoundException"/> with a diagnostic message.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    public SymbolNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="SymbolNotFoundException"/> that wraps an inner exception.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    /// <param name="innerException">The underlying exception that triggered this one.</param>
    public SymbolNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
