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

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Requests;

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Builds a <see cref="DividendPaymentDatasetKey"/> from a <see cref="DividendHistoryRequest"/>.
/// The provider is resolved at the adapter-selection layer and supplied here via
/// <see cref="ProviderName"/>; the ticker and optional exchange come from the request. The
/// two-field durable key keeps venue identity inside its ticker coordinate: slash characters in
/// the opaque ticker are escaped before the ticker is venue-qualified. This is
/// the request → dataset-key projection the time-series row-store path uses to resolve the live
/// <see cref="Boutquin.MarketData.Abstractions.Records.DividendPayment"/> store and compute gaps.
/// </summary>
/// <param name="ProviderName">Stable string identifier for the upstream source (e.g., <c>"iex"</c>).</param>
/// <remarks>
/// <para>
/// A <see cref="DividendHistoryRequest"/> carries a list of <c>Symbols</c> because it is also a
/// batch request; the pipeline partitions a multi-symbol batch into one single-symbol request per
/// partition <b>before</b> reaching the time-series store, so <see cref="Build"/> keys on the
/// request's first symbol (the partition's ticker). A request with no symbols is a programming
/// error and throws.
/// </para>
/// <para>
/// Deliberately mirrors <see cref="PriceBarDatasetKeyBuilder"/>'s post-split contract: the
/// symbol count is guarded only against zero, never asserted to equal one. The batch splitter is
/// what guarantees single-symbol arrival on the production path, so an equality assert here would
/// add a second, redundant enforcement point that could reject a legitimately pre-partitioned
/// request a caller built by hand.
/// </para>
/// <para>
/// The key has no frequency or venue column because an ex-date resolves to a calendar day and the
/// durable wire remains <c>(Provider, Ticker)</c>. Venue qualification is encoded into the ticker
/// coordinate; venue-neutral requests retain the legacy two-coordinate identity.
/// </para>
/// </remarks>
public sealed record DividendPaymentDatasetKeyBuilder(string ProviderName)
    : IDatasetKeyBuilder<DividendHistoryRequest, DividendPaymentDatasetKey>,
      IProviderAwareDatasetKeyBuilder<DividendHistoryRequest, DividendPaymentDatasetKey>
{
    private string _providerName = new ProviderCode(ProviderName).Value;

    /// <summary>The canonical lower-case provider identifier used in durable keys.</summary>
    public string ProviderName
    {
        get => _providerName;
        init => _providerName = new ProviderCode(value).Value;
    }

    /// <inheritdoc />
    public DividendPaymentDatasetKey Build(DividendHistoryRequest request)
        => BuildCore(request, ProviderName);

    DividendPaymentDatasetKey IProviderAwareDatasetKeyBuilder<DividendHistoryRequest, DividendPaymentDatasetKey>.Build(
        DividendHistoryRequest request,
        ProviderCode provider)
        => BuildCore(request, provider.Value);

    private static DividendPaymentDatasetKey BuildCore(
        DividendHistoryRequest request,
        string providerName)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Symbols.Count == 0)
        {
            throw new ArgumentException(
                "A DividendHistoryRequest reaching the time-series store must carry exactly one symbol "
                + "(the pipeline partitions multi-symbol batches per partition before key building); "
                + "the request carried none.",
                nameof(request));
        }

        return new DividendPaymentDatasetKey(
            providerName,
            request.Symbols[0].Ticker,
            request.Exchange);
    }
}
