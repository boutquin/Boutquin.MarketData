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
/// Thrown when a requested data resource is known to the system but not present
/// for the requested key / date / venue combination.
/// </summary>
/// <remarks>
/// Distinguishes from <see cref="MarketDataRetrievalException"/>, which indicates
/// a transport-layer failure (provider timeout, authentication error). Use
/// <see cref="MarketDataNotFoundException"/> when the provider responded
/// successfully but had no record for the requested resource.
/// </remarks>
public sealed class MarketDataNotFoundException : MarketDataException
{
    /// <summary>
    /// Initializes a new <see cref="MarketDataNotFoundException"/> with no message.
    /// </summary>
    public MarketDataNotFoundException()
    {
    }

    /// <summary>
    /// Initializes a new <see cref="MarketDataNotFoundException"/> with a diagnostic message.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    public MarketDataNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="MarketDataNotFoundException"/> that wraps an inner exception.
    /// </summary>
    /// <param name="message">The explanation of the failure.</param>
    /// <param name="innerException">The underlying exception that triggered this one.</param>
    public MarketDataNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
