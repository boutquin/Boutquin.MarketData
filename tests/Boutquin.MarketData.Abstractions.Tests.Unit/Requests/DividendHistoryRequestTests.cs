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
using Boutquin.MarketData.Abstractions.Requests;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Requests;

public sealed class DividendHistoryRequestTests
{
    private static readonly DateRange s_range = new(
        new DateOnly(2024, 1, 1),
        new DateOnly(2024, 12, 31));

    [Fact]
    public void DatasetKey_IsStable_WhenCallerMutatesTheSymbolCollectionAfterConstruction()
    {
        // The cache key must be a function of the request as constructed. If Symbols aliased the
        // caller's list, a later Add would silently re-point the request at a different snapshot.
        var symbols = new List<Symbol> { new("MSFT") };
        var request = new DividendHistoryRequest(symbols, s_range);

        symbols.Add(new Symbol("AAPL"));

        request.DatasetKey.Should().Be("MSFT");
        request.Symbols.Should().ContainSingle().Which.Ticker.Should().Be("MSFT");
    }

    [Fact]
    public void Constructor_RejectsAnEmptySymbolCollection()
    {
        var construct = () => new DividendHistoryRequest([], s_range);

        construct.Should().Throw<ArgumentOutOfRangeException>(
            because: "an empty request cannot produce a valid snapshot dataset key");
    }

    [Fact]
    public void Constructor_RejectsDuplicateSymbols()
    {
        var construct = () => new DividendHistoryRequest(
            [new Symbol("SCHD"), new Symbol("SCHD")],
            s_range);

        construct.Should().Throw<ArgumentException>(
                because: "duplicate partitions multiply returned dividend records even when the fetch is coalesced")
            .WithParameterName("symbols");
    }

    [Fact]
    public void Constructor_PreservesCaseDistinctSymbols()
    {
        var request = new DividendHistoryRequest([new Symbol("SCHD"), new Symbol("schd")], s_range);
        var reordered = new DividendHistoryRequest([new Symbol("schd"), new Symbol("SCHD")], s_range);

        request.Symbols.Should().HaveCount(2);
        request.DatasetKey.Should().Be(reordered.DatasetKey);
    }

    [Fact]
    public void Constructor_RejectsADefaultSymbolBeforeDatasetKeyAccess()
    {
        var construct = () => new DividendHistoryRequest([default], s_range);

        construct.Should().Throw<ArgumentException>(
                because: "a value-type default must be rejected at the request boundary, not fail later while keying")
            .WithParameterName("symbols");
    }

    [Fact]
    public void WithSymbols_RejectsADefaultSymbolBeforeDatasetKeyAccess()
    {
        var request = new DividendHistoryRequest([new Symbol("SCHD")], s_range);

        var assign = () => _ = request with { Symbols = [default] };

        assign.Should().Throw<ArgumentException>(
                because: "record cloning must enforce the same symbol invariant as construction")
            .WithParameterName("symbols");
    }

    [Fact]
    public void DatasetKey_IsStable_WhenCallerMutatesSymbolsAssignedThroughWithInitializer()
    {
        var replacement = new List<Symbol> { new("VYM") };
        var request = new DividendHistoryRequest([new Symbol("SCHD")], s_range)
        {
            Symbols = replacement,
        };

        replacement.Add(new Symbol("SPY"));

        request.DatasetKey.Should().Be("VYM");
        request.Symbols.Should().ContainSingle().Which.Ticker.Should().Be("VYM");
    }

    [Fact]
    public void DatasetKey_OmitsTheExchangeSuffix_WhenVenueNeutral_ButAppendsIt_WhenScoped()
    {
        // "SCHD on XNYS" and venue-neutral "SCHD" are different requests and must not share a
        // snapshot; the suffix is what partitions the cache into a sub-directory per venue.
        var symbols = new[] { new Symbol("SCHD") };
        var venueNeutral = new DividendHistoryRequest(symbols, s_range);
        var onXnys = new DividendHistoryRequest(symbols, s_range) { Exchange = ExchangeCode.XNYS };

        venueNeutral.DatasetKey.Should().Be("SCHD");
        onXnys.DatasetKey.Should().Be("SCHD/XNYS");
    }

    [Fact]
    public void WithRange_NarrowsOnlyTheRange_PreservingSymbolsAndExchange()
    {
        // Gap-fill re-dispatches the request for a missing sub-range. Losing the venue hint here
        // would silently widen adapter selection and write the gap under a different cache key.
        var gap = new DateRange(new DateOnly(2024, 3, 1), new DateOnly(2024, 3, 31));
        var request = new DividendHistoryRequest([new Symbol("SCHD"), new Symbol("VYM")], s_range)
        {
            Exchange = ExchangeCode.XNYS,
        };

        var narrowed = request.WithRange(gap).Should().BeOfType<DividendHistoryRequest>().Subject;

        narrowed.Range.Should().Be(gap);
        narrowed.Symbols.Should().Equal(request.Symbols);
        narrowed.Exchange.Should().Be(ExchangeCode.XNYS);
        narrowed.DatasetKey.Should().Be(request.DatasetKey);
    }

    [Fact]
    public void DatasetKey_JoinsMultipleTickersInCaseInsensitiveOrder_AndReducesToTheBareTickerForOne()
    {
        // A single-symbol key is the bare ticker — that is the key a partitioned request produces,
        // so it must not carry separator residue. A multi-symbol key is order-independent, so two
        // callers passing the same basket in different orders hit the same snapshot.
        var single = new DividendHistoryRequest([new Symbol("SCHD")], s_range);
        var multi = new DividendHistoryRequest([new Symbol("msft"), new Symbol("AAPL")], s_range);
        var multiReordered = new DividendHistoryRequest([new Symbol("AAPL"), new Symbol("msft")], s_range);

        single.DatasetKey.Should().Be("SCHD");
        multi.DatasetKey.Should().Be("_D2_004100410050004C_006D007300660074");
        multiReordered.DatasetKey.Should().Be(multi.DatasetKey);
    }

    [Fact]
    public void DatasetKey_PreservesPriceHistoryIdentityForLegacySafeSingletons()
    {
        // Established separator-free single-symbol keys remain byte-identical to price history;
        // multi-symbol dividend keys use an injective list encoding because opaque tickers may
        // themselves contain the price family's underscore separator.
        string[][] symbolSets =
        [
            ["SCHD"],
            ["BRK.B"],
            ["SHOP"],
        ];
        ExchangeCode?[] venues = [null, ExchangeCode.XNYS, ExchangeCode.XTSE];

        foreach (var tickers in symbolSets)
        {
            var symbols = tickers.Select(t => new Symbol(t)).ToArray();

            foreach (var venue in venues)
            {
                var dividends = new DividendHistoryRequest(symbols, s_range) { Exchange = venue };
                var prices = new PriceHistoryRequest(symbols, s_range) { Exchange = venue };

                dividends.DatasetKey.Should().Be(
                    prices.DatasetKey,
                    because: "tickers [{0}] on venue {1} must key identically across request families",
                    string.Join(",", tickers),
                    venue?.ToString() ?? "(none)");
            }
        }
    }

    [Fact]
    public void DatasetKey_IsInvariantUnderEveryPermutationOfFourDistinctSymbols()
    {
        string[] tickers = ["VYM", "schd", "DGRO", "SPY"];
        var expected = new DividendHistoryRequest(
            tickers.Select(t => new Symbol(t)).ToArray(),
            s_range).DatasetKey;
        var observedInputs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var a in Enumerable.Range(0, tickers.Length))
        {
            foreach (var b in Enumerable.Range(0, tickers.Length).Where(i => i != a))
            {
                foreach (var c in Enumerable.Range(0, tickers.Length).Where(i => i != a && i != b))
                {
                    foreach (var d in Enumerable.Range(0, tickers.Length).Where(i => i != a && i != b && i != c))
                    {
                        string[] permutation = [tickers[a], tickers[b], tickers[c], tickers[d]];
                        observedInputs.Add(string.Join("|", permutation));

                        new DividendHistoryRequest(
                            permutation.Select(t => new Symbol(t)).ToArray(),
                            s_range).DatasetKey.Should().Be(expected);
                    }
                }
            }
        }

        observedInputs.Should().HaveCount(24, "4! distinct permutations must all be exercised");
    }

    [Fact]
    public void DatasetKey_IsInjectiveAcrossSeededOpaqueSymbolLists()
    {
        const int seed = 52058;
        var random = new Random(seed);
        string[] alphabet = ["A", "B", "C", "_", "/", ".", "7"];
        List<string[]> symbolLists =
        [
            ["A_B"],
            ["A", "B"],
            ["A_", "B"],
            ["A", "_B"],
            ["A/B_C"],
            ["A/B", "C"],
            ["A", "B/C"],
            ["_A", "B/C", "D_"],
        ];

        while (symbolLists.Count < 256)
        {
            var count = random.Next(1, 5);
            var tickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (tickers.Count < count)
            {
                var length = random.Next(1, 9);
                var ticker = string.Concat(
                    Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]));
                tickers.Add(ticker);
            }

            symbolLists.Add([.. tickers]);
        }

        var keyOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tickers in symbolLists)
        {
            var logicalIdentity = string.Join(
                "|",
                tickers
                    .OrderBy(ticker => ticker, StringComparer.OrdinalIgnoreCase)
                    .Select(ticker => $"{ticker.Length}:{ticker}"));
            var key = new DividendHistoryRequest(
                tickers.Select(ticker => new Symbol(ticker)).ToArray(),
                s_range).DatasetKey;

            if (keyOwners.TryGetValue(key, out var priorOwner))
            {
                priorOwner.Should().Be(
                    logicalIdentity,
                    because: "seed {0} generated distinct opaque symbol lists that must never alias key {1}",
                    seed,
                    key);
            }
            else
            {
                keyOwners.Add(key, logicalIdentity);
            }
        }
    }
}
