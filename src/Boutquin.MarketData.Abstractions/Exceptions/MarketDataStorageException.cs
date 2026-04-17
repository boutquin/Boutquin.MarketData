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
/// Thrown when a snapshot, cache, or raw-document persistence step fails —
/// disk full, permission denied, serialization error, or integrity check
/// failure.
/// </summary>
/// <remarks>
/// Distinguished from <see cref="MarketDataRetrievalException"/> (upstream
/// fetch failed) so callers can implement retry policies appropriate to the
/// storage backend.
/// </remarks>
public sealed class MarketDataStorageException : MarketDataException
{
    /// <summary>
    /// Initializes a new <see cref="MarketDataStorageException"/> with no message.
    /// </summary>
    public MarketDataStorageException()
    {
    }

    /// <summary>
    /// Initializes a new <see cref="MarketDataStorageException"/> with a diagnostic message.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    public MarketDataStorageException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="MarketDataStorageException"/> that wraps an inner exception.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    /// <param name="innerException">The underlying exception that triggered this one.</param>
    public MarketDataStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
