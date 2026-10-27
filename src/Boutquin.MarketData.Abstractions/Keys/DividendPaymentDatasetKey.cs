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

using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.Storage.Domain.Attributes;

namespace Boutquin.MarketData.Abstractions.Keys;

/// <summary>
/// Composite partition key for a ticker's dividend payment stream:
/// <c>(Provider, Ticker)</c>. Sort-defining timestamp lives on the value record
/// (<see cref="Records.DividendPayment.ExDate"/>), not the key.
/// </summary>
/// <param name="Provider">Adapter-assigned provider identifier.</param>
/// <param name="Ticker">Encoded ticker coordinate whose dividend schedule this key addresses.</param>
/// <remarks>
/// See <see cref="PriceBarDatasetKey"/> remarks for the Storage 2.0 tagged-ordered-wire design and
/// lexicographic ordering guarantee. Both fields stay <c>string</c> — provider is an open registry
/// and the ticker is a source-opaque identifier with no closed vocabulary to enum-substitute.
/// The two-coordinate wire stays unchanged for existing callers and stored neutral keys. The
/// three-argument constructor escapes slash characters in the opaque ticker before it venue-qualifies
/// that ticker coordinate with the request-key separator; venue-scoped requests intentionally use new
/// physical identities and do not reuse legacy neutral data.
/// </remarks>
[Key]
public readonly partial record struct DividendPaymentDatasetKey(
    [property: Tag(1)] string Provider,
    [property: Tag(2)] string Ticker)
{
    /// <summary>Creates a key whose ticker coordinate includes an optional listing venue.</summary>
    /// <param name="Provider">Adapter-assigned provider identifier.</param>
    /// <param name="Ticker">Opaque ticker symbol encoded into the durable ticker coordinate.</param>
    /// <param name="Exchange">Optional listing venue; <see langword="null"/> for the legacy neutral identity.</param>
    public DividendPaymentDatasetKey(string Provider, string Ticker, ExchangeCode? Exchange)
        : this(Provider, DividendDatasetIdentity.EncodeTickerAndExchange(Ticker, Exchange))
    {
    }
}
