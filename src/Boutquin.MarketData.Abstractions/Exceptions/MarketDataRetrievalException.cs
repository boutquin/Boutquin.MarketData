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
/// Thrown when a transport-layer or provider error prevents a data request
/// from completing — network failure, timeout, authentication error, provider
/// 5xx response, or rate-limit rejection.
/// </summary>
/// <remarks>
/// Callers frequently treat <see cref="MarketDataRetrievalException"/> as
/// retryable, unlike <see cref="MarketDataNotFoundException"/> which indicates
/// the provider answered successfully but had no record. The concrete cause
/// should be chained via the <see cref="Exception.InnerException"/>.
/// </remarks>
public sealed class MarketDataRetrievalException : MarketDataException
{
    /// <summary>
    /// Initializes a new <see cref="MarketDataRetrievalException"/> with no message.
    /// </summary>
    public MarketDataRetrievalException()
    {
    }

    /// <summary>
    /// Initializes a new <see cref="MarketDataRetrievalException"/> with a diagnostic message.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    public MarketDataRetrievalException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="MarketDataRetrievalException"/> that wraps an inner exception.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    /// <param name="innerException">The underlying exception that triggered this one.</param>
    public MarketDataRetrievalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
