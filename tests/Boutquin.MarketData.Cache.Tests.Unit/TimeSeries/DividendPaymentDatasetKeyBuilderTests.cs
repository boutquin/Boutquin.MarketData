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
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit.TimeSeries;

/// <summary>
/// Behavioral contract for <see cref="DividendPaymentDatasetKeyBuilder"/>, pinned against the
/// strongly typed surface: the post-split projection, the null guard, and the deliberate
/// absence of a single-symbol assert that the sibling price-bar builder also omits.
/// </summary>
public sealed class DividendPaymentDatasetKeyBuilderTests
{
    private static readonly DateRange s_range =
        new(new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31));

    [Fact]
    public void Build_NullRequest_ThrowsArgumentNullException()
    {
        var builder = new DividendPaymentDatasetKeyBuilder("iex");
        DividendHistoryRequest request = null!;

        var act = () => builder.Build(request);

        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("request");
    }

    [Fact]
    public void Build_MultiSymbolRequest_ProjectsTheFirstTicker_WithoutAssertingASingleSymbol()
    {
        var builder = new DividendPaymentDatasetKeyBuilder("iex");
        var request = new DividendHistoryRequest(
            [new Symbol("AAPL"), new Symbol("MSFT"), new Symbol("SCHD")],
            s_range);

        var act = () => builder.Build(request);

        // The sibling price-bar builder deliberately guards only the zero case: the batch splitter
        // is the single enforcement point for single-symbol arrival, so a redundant Count == 1
        // assert here would reject a hand-partitioned request the production path never produces.
        // Adding such an assert must break this test — that mutation is the pin's proof.
        act.Should().NotThrow();
        act().Should().Be(new DividendPaymentDatasetKey("iex", "AAPL"));
    }

    [Fact]
    public void Provider_aware_build_uses_the_fallback_selected_provider()
    {
        IProviderAwareDatasetKeyBuilder<DividendHistoryRequest, DividendPaymentDatasetKey> builder =
            new DividendPaymentDatasetKeyBuilder("default");
        var request = new DividendHistoryRequest([new Symbol("SCHD")], s_range);

        var key = builder.Build(request, new ProviderCode("tiingo"));

        key.Should().Be(new DividendPaymentDatasetKey("tiingo", "SCHD"));
    }

    [Theory]
    [InlineData("Tiingo", "tiingo")]
    [InlineData("IEX", "iex")]
    [InlineData("Mixed-Provider", "mixed-provider")]
    public void Direct_and_provider_aware_builds_share_the_canonical_provider_identity(
        string directProvider,
        string expectedProvider)
    {
        var direct = new DividendPaymentDatasetKeyBuilder(directProvider);
        IProviderAwareDatasetKeyBuilder<DividendHistoryRequest, DividendPaymentDatasetKey> providerAware =
            new DividendPaymentDatasetKeyBuilder("default");
        var request = new DividendHistoryRequest([new Symbol("SCHD")], s_range);

        var directKey = direct.Build(request);
        var providerAwareKey = providerAware.Build(request, new ProviderCode(directProvider));

        direct.ProviderName.Should().Be(expectedProvider);
        directKey.Should().Be(providerAwareKey);
        directKey.Provider.Should().Be(expectedProvider);
    }

    [Fact]
    public void Direct_build_preserves_distinct_provider_identities_and_the_existing_tiingo_wire()
    {
        var request = new DividendHistoryRequest([new Symbol("SCHD")], s_range);

        var tiingo = new DividendPaymentDatasetKeyBuilder("tiingo").Build(request);
        var iex = new DividendPaymentDatasetKeyBuilder("iex").Build(request);

        tiingo.Should().Be(new DividendPaymentDatasetKey("tiingo", "SCHD"));
        iex.Should().NotBe(tiingo);
    }

    [Fact]
    public void ProviderName_InitCanonicalizesAndRejectsInvalidValues()
    {
        var builder = new DividendPaymentDatasetKeyBuilder("default") { ProviderName = "Tiingo" };
        var request = new DividendHistoryRequest([new Symbol("SCHD")], s_range);

        builder.ProviderName.Should().Be("tiingo");
        builder.Build(request).Provider.Should().Be("tiingo");

        var cloned = builder with { ProviderName = "IEX" };
        cloned.ProviderName.Should().Be("iex");
        cloned.Build(request).Provider.Should().Be("iex");

        var invalid = () => new DividendPaymentDatasetKeyBuilder("default") { ProviderName = "   " };
        invalid.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_preserves_exchange_as_part_of_durable_dataset_identity()
    {
        var builder = new DividendPaymentDatasetKeyBuilder("tiingo");
        var onNyse = new DividendHistoryRequest([new Symbol("SHOP")], s_range)
        {
            Exchange = ExchangeCode.XNYS,
        };
        var onToronto = new DividendHistoryRequest([new Symbol("SHOP")], s_range)
        {
            Exchange = ExchangeCode.XTSE,
        };

        builder.Build(onNyse).Should().NotBe(
            builder.Build(onToronto),
            "the same provider ticker on different listing venues must use different durable datasets");
    }

    [Fact]
    public void Build_separates_opaque_separator_ticker_from_venue_scoped_ticker_on_both_key_surfaces()
    {
        var builder = new DividendPaymentDatasetKeyBuilder("tiingo");
        var opaqueTicker = new DividendHistoryRequest([new Symbol("SHOP/XNYS")], s_range);
        var venueScoped = new DividendHistoryRequest([new Symbol("SHOP")], s_range)
        {
            Exchange = ExchangeCode.XNYS,
        };

        opaqueTicker.DatasetKey.Should().NotBe(
            venueScoped.DatasetKey,
            "an opaque ticker containing the venue separator is not the same identity as a venue-scoped ticker");
        builder.Build(opaqueTicker).Should().NotBe(
            builder.Build(venueScoped),
            "durable identity must preserve the same ticker/venue distinction as the snapshot key");
    }
}
