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

/// <summary>Refuses cached, merged or snapshot prices whose basis cannot satisfy the request.</summary>
public sealed class PriceBasisMismatchException : MarketDataException
{
    /// <summary>Records the requested and offending stored basis without changing either.</summary>
    /// <param name="requested">The requested basis.</param>
    /// <param name="stored">The stated offending basis, or null when a partition merge is unproven.</param>
    /// <param name="provider">Canonical provider code; partition-merge for an unproven merge.</param>
    /// <exception cref="ArgumentNullException">The provider is null.</exception>
    public PriceBasisMismatchException(PriceBasis requested, PriceBasis? stored, string provider)
        : base(stored is null
            ? $"price basis unproven for provider '{provider}'; requested '{requested.Token}'."
            : $"Price basis '{stored.Value.Token}' from provider '{provider}' differs from requested '{requested.Token}'.")
    {
        ArgumentNullException.ThrowIfNull(provider);
        Requested = requested;
        Stored = stored;
        Provider = provider;
    }
    /// <summary>The requested price basis.</summary>
    public PriceBasis Requested { get; }
    /// <summary>The offending stated basis, or null for an unproven partition merge.</summary>
    public PriceBasis? Stored { get; }
    /// <summary>The canonical provider code retained exactly as supplied.</summary>
    public string Provider { get; }
}
