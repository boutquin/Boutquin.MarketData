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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>
/// Requests the cash-dividend history for one or more equity/ETF symbols over a date range,
/// yielding one <see cref="Records.DividendPayment"/> per ex-date on which cash was distributed.
/// </summary>
/// <param name="Symbols">The non-empty strongly-typed ticker symbols whose distributions to fetch.</param>
/// <param name="Range">The inclusive ex-date range to cover.</param>
/// <remarks>
/// <para><b>Pipeline context:</b> a batch request over <see cref="Symbols"/>, dispatched through
/// <see cref="IDataPipeline.ExecuteAsync{TRequest,TRecord}"/> with
/// <see cref="Records.DividendPayment"/> as the record type. It reuses
/// <see cref="PriceHistoryRequest"/>'s batch partitioning and range-narrowing shape —
/// <see cref="PartitionKeys"/> is backed by <see cref="Symbols"/> and <see cref="WithRange"/>
/// preserves request identity — while <see cref="DatasetKey"/> uses the shared,
/// collision-safe symbol encoding described below.</para>
/// <para><b>Event series, not a sampled series:</b> distributions occur on a handful of ex-dates
/// per year, so most calendar days inside <paramref name="Range"/> carry no record. A response
/// containing far fewer records than the range has trading days is the expected shape, not a
/// coverage gap. <see cref="Frequency"/> is fixed at <see cref="DataFrequency.Daily"/> because an
/// ex-date resolves to a calendar day; there is no sampling choice for a consumer to make, which
/// is why the request takes no frequency parameter.</para>
/// <para><b>Consumer boundary:</b> a default host returns raw per-payment records; a host that opts
/// into or explicitly registers the dividend normalizer receives clamped and rounded amounts.
/// Deriving an income yield from either form (summing amounts over a trailing window and dividing
/// by an as-of price) is portfolio analytics, not data-kernel work.</para>
/// <para><b>Cache-key coordinates:</b> the snapshot tier keys each dividend entry on the exact
/// requested <see cref="IDataRequest.Range"/>. Only another dividend request with that same range
/// can reuse the entry; a price request has a distinct record-type identity regardless of whether
/// its bounds match.</para>
/// <para><b>Consumer guide:</b>
/// <see href="https://github.com/boutquin/Boutquin.MarketData/blob/main/docs/dividends.md">Dividend History and Trailing Income Yield</see> is the contract
/// page for this request — the ownership boundary above with a worked trailing-12M example, the
/// host-side <c>INormalizer&lt;DividendPayment, DividendPayment&gt;</c> registration a default host
/// needs (the kernel's own registration is gated off by default), the exact-range cache rule, and
/// why a symbol with no ex-dates in the window returns an empty success rather than an error.</para>
/// </remarks>
/// <seealso cref="PriceHistoryRequest"/>
/// <seealso cref="Records.DividendPayment"/>
public sealed record DividendHistoryRequest(
    IReadOnlyList<Symbol> Symbols,
    DateRange Range) : IDataRequest, IBatchDataRequest<Symbol>
{
    /// <summary>
    /// The ticker symbols being requested, stored as a defensive copy so mutation of the
    /// caller's collection cannot change <see cref="DatasetKey"/> stability or record equality.
    /// </summary>
    public IReadOnlyList<Symbol> Symbols
    {
        get;
        init => field = CopyAndValidate(value);
    } = CopyAndValidate(Symbols);

    /// <summary>
    /// Optional listing venue hint — when set, adapter-selection orchestrators can filter to
    /// sources that cover the specified exchange. When <see langword="null"/> the request is
    /// venue-neutral and any adapter may serve it. Also scopes <see cref="DatasetKey"/> so two
    /// requests for the same ticker on different exchanges produce distinct cache entries.
    /// </summary>
    /// <remarks>
    /// Exposed as an <c>init</c> property rather than a primary-constructor parameter so that a
    /// later parameter addition remains binary-compatible for consumers compiled against the
    /// current primary-constructor signature — the same convention the sibling requests follow.
    /// Set via object-initializer:
    /// <c>new DividendHistoryRequest(symbols, range) { Exchange = ExchangeCode.XNYS }</c>.
    /// </remarks>
    public ExchangeCode? Exchange { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// Uses an injective symbol-list encoding after ordering tickers
    /// case-insensitively. A separator-free single-symbol request retains its legacy ticker text;
    /// multi-symbol requests and ticker text containing the list separator use a count-prefixed
    /// UTF-16 encoding so an opaque ticker cannot alias a different list. A slash inside ticker
    /// text is escaped before an optional <c>/</c> plus <see cref="Exchange"/> venue suffix is appended.
    /// This matches <see cref="PriceHistoryRequest.DatasetKey"/> while keeping established neutral
    /// singleton keys stable. The pipeline's record-type coordinate separates dividends from prices.
    /// </remarks>
    public string DatasetKey
    {
        get
        {
            var symbolsKey = DividendDatasetIdentity.EncodeSymbolList(
                Symbols.Select(symbol => symbol.Ticker));
            return Exchange is null ? symbolsKey : $"{symbolsKey}/{Exchange.Value}";
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns the <see cref="Symbols"/> property itself rather than a projection. The batch
    /// splitter partitions a multi-symbol request by locating the writable property whose value
    /// is this very collection and overwriting it on a record clone; a freshly projected list
    /// would break that lookup and leave the request unpartitionable.
    /// </remarks>
    public IReadOnlyList<Symbol> PartitionKeys => Symbols;

    /// <summary>
    /// Fixed at <see cref="DataFrequency.Daily"/>: an ex-date resolves to a single calendar day,
    /// so daily is the only granularity at which a distribution can be observed.
    /// </summary>
    /// <value>Always <see cref="DataFrequency.Daily"/>.</value>
    public DataFrequency Frequency => DataFrequency.Daily;

    /// <inheritdoc />
    /// <remarks>
    /// Preserves the symbol set and the optional <see cref="Exchange"/> venue hint; only
    /// <see cref="IDataRequest.Range"/> is narrowed. The time-series gap-fill path uses this to
    /// dispatch an adapter for a single missing ex-date sub-range rather than re-fetching the
    /// whole requested window.
    /// </remarks>
    public IDataRequest WithRange(DateRange range) =>
        new DividendHistoryRequest(Symbols, range) { Exchange = Exchange };

    private static IReadOnlyList<Symbol> CopyAndValidate(IReadOnlyList<Symbol> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentOutOfRangeException.ThrowIfZero(symbols.Count);

        var distinctTickers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (symbol.Ticker is null)
            {
                throw new ArgumentException(
                    "Dividend history symbols cannot contain a default or null-ticker symbol.",
                    nameof(symbols));
            }

            if (!distinctTickers.Add(symbol.Ticker))
            {
                throw new ArgumentException(
                    $"Dividend history symbols must be unique; ticker '{symbol.Ticker}' appears more than once.",
                    nameof(symbols));
            }
        }

        return [.. symbols];
    }
}
