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
/// Base class for every failure mode exposed by the MarketData kernel.
/// </summary>
/// <remarks>
/// <para>
/// Having a shared base lets consumers catch the whole family with a single
/// handler — the previous scattering of exception types across
/// <c>Boutquin.Trading.Domain.Exceptions</c> forced callers to enumerate each
/// concrete type or fall back to <see cref="Exception"/>. Specific subclasses
/// (<see cref="MarketDataNotFoundException"/>, <see cref="MarketDataRetrievalException"/>,
/// <see cref="MarketDataProcessingException"/>, <see cref="MarketDataStorageException"/>,
/// <see cref="SymbolNotFoundException"/>) preserve failure-mode distinction for
/// callers that care.
/// </para>
/// <para>
/// <see cref="MarketDataException"/> is abstract — callers always throw one of the
/// concrete subclasses (or a private subtype in product code). Catching the base
/// type captures every failure in the family with a single handler.
/// </para>
/// </remarks>
public abstract class MarketDataException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="MarketDataException"/> with no message.
    /// </summary>
    protected MarketDataException()
    {
    }

    /// <summary>
    /// Initializes a new <see cref="MarketDataException"/> with a diagnostic message.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    protected MarketDataException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="MarketDataException"/> that wraps an inner exception.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    /// <param name="innerException">The underlying exception that triggered this one.</param>
    protected MarketDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
