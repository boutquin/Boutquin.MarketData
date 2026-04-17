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
/// Thrown when a normalization, parsing, or post-retrieval transformation step
/// cannot complete — malformed provider payload, schema mismatch, or a
/// normalization invariant violated.
/// </summary>
/// <remarks>
/// Distinct from <see cref="MarketDataRetrievalException"/> (transport failed)
/// and <see cref="MarketDataStorageException"/> (persistence failed). A
/// processing failure is typically not retryable without a fix in the
/// normalizer or producer.
/// </remarks>
public sealed class MarketDataProcessingException : MarketDataException
{
    /// <summary>
    /// Initializes a new <see cref="MarketDataProcessingException"/> with no message.
    /// </summary>
    public MarketDataProcessingException()
    {
    }

    /// <summary>
    /// Initializes a new <see cref="MarketDataProcessingException"/> with a diagnostic message.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    public MarketDataProcessingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="MarketDataProcessingException"/> that wraps an inner exception.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    /// <param name="innerException">The underlying exception that triggered this one.</param>
    public MarketDataProcessingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
