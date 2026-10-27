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
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit.TimeSeries;

// M-5 dataset-key builder tests. Each IDatasetKeyBuilder<TRequest, TDatasetKey> projects a
// concrete request's identity fields into its corresponding DatasetKey, stringifying enums
// and typed wrappers at this boundary so the DatasetKey struct stays primitive-only.
public sealed class DatasetKeyBuilderTests
{
    private static readonly DateRange s_range = new(new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31));

    [Fact]
    public void EconomicSeriesDatasetKeyBuilder_ProjectsProviderAndSeriesId()
    {
        var request = new EconomicSeriesRequest(new EconomicSeriesId("DGS10"), s_range);
        var builder = new EconomicSeriesDatasetKeyBuilder("fred");

        var key = builder.Build(request);

        key.Should().Be(new ScalarObservationDatasetKey("fred", "DGS10"));
    }

    [Fact]
    public void EconomicSeriesDatasetKeyBuilder_IgnoresExchangeHint()
    {
        // Economic series are regionally unique — Exchange is adapter-selection input, not key material.
        var req = new EconomicSeriesRequest(new EconomicSeriesId("GDP"), s_range) { Exchange = ExchangeCode.XNYS };
        var key = new EconomicSeriesDatasetKeyBuilder("fred").Build(req);

        key.Should().Be(new ScalarObservationDatasetKey("fred", "GDP"));
    }

    [Fact]
    public void EconomicSeriesDatasetKeyBuilder_NullRequest_Throws()
    {
        var builder = new EconomicSeriesDatasetKeyBuilder("fred");

        var act = () => builder.Build(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FactorSeriesDatasetKeyBuilder_ProjectsProviderDatasetFrequency()
    {
        var request = new FactorSeriesRequest(new FactorDatasetId("F-F_3Factors_daily"), s_range, DataFrequency.Daily);
        var builder = new FactorSeriesDatasetKeyBuilder("famafrench");

        var key = builder.Build(request);

        // M-CR: Frequency is the DataFrequency enum (no stringify). FactorName is left empty by the
        // single-key builder — the request addresses a whole factor set; per-factor fan-out is M-7.
        key.Should().Be(new FactorObservationDatasetKey("famafrench", "F-F_3Factors_daily", DataFrequency.Daily, string.Empty));
    }

    [Fact]
    public void FuturesSettlementDatasetKeyBuilder_ProjectsProviderAndProductCode()
    {
        var request = new FuturesSettlementRequest(new FuturesProductCode("SR3"), s_range);
        var builder = new FuturesSettlementDatasetKeyBuilder("cme");

        var key = builder.Build(request);

        // ContractMonth left empty at M-5 — per-month partitioning is follow-up work.
        key.Should().Be(new FuturesSettlementDatasetKey("cme", "SR3", string.Empty));
    }

    [Fact]
    public void YieldCurveQuoteDatasetKeyBuilder_ProjectsProviderAndCurveId()
    {
        var request = new YieldCurveQuoteRequest(new YieldCurveId("CAD-ZERO"), new DateOnly(2026, 1, 2));
        var builder = new YieldCurveQuoteDatasetKeyBuilder("boc");

        var key = builder.Build(request);

        // CurveId column = request.DatasetKey (folds the optional /Exchange hint); Tenor empty
        // (per-node fan-out is M-7), mirroring the factor / futures-month single-key builders.
        key.Should().Be(new YieldCurveQuoteDatasetKey("boc", "CAD-ZERO", string.Empty));
    }

    [Fact]
    public void YieldCurveQuoteDatasetKeyBuilder_FoldsExchangeIntoCurveId()
    {
        var request = new YieldCurveQuoteRequest(new YieldCurveId("CAD-ZERO"), new DateOnly(2026, 1, 2))
        {
            Exchange = ExchangeCode.XNYS,
        };
        var builder = new YieldCurveQuoteDatasetKeyBuilder("boc");

        var key = builder.Build(request);

        // The regional hint folds into CurveId at the request layer (Q17): "CAD-ZERO/XNYS".
        key.Should().Be(new YieldCurveQuoteDatasetKey("boc", "CAD-ZERO/XNYS", string.Empty));
    }
}
