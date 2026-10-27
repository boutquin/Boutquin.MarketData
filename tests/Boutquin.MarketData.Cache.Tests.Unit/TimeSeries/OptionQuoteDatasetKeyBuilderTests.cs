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
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit.TimeSeries;

// M-2.7 OptionQuoteDatasetKeyBuilder tests. An OptionChainRequest addresses a whole option chain for
// one underlying over a date range — the per-contract discriminants (Expiry/Strike/Type) are NOT
// request inputs, they are per-row data. So the single-key builder projects only Provider + Underlying
// and leaves Expiry/Strike/Type at their defaults, exactly as the factor / futures-month / yield-tenor
// single-key builders leave their per-row discriminant empty: per-contract fan-out (one key per
// contract) is a pipeline-layer concern wired when the time-series read path lands (M-7 / Q19), not at
// this single-identity builder. These tests pin the wired Build() projection.
public sealed class OptionQuoteDatasetKeyBuilderTests
{
    private static readonly DateRange s_range = new(new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31));

    [Fact]
    public void Build_ProjectsProviderAndUnderlying()
    {
        var request = new OptionChainRequest("AAPL", s_range);
        var builder = new OptionQuoteDatasetKeyBuilder("opra");

        var key = builder.Build(request);

        // Provider + Underlying are the only identity fields an OptionChainRequest carries; the
        // per-contract discriminants stay at defaults (Expiry = default, Strike = 0m, Type = Call).
        key.Should().Be(new OptionQuoteDatasetKey("opra", "AAPL", default, 0m, OptionType.Call));
        key.Provider.Should().Be("opra");
        key.Underlying.Should().Be("AAPL");
    }

    [Fact]
    public void Build_NullRequest_Throws()
    {
        var builder = new OptionQuoteDatasetKeyBuilder("opra");

        var act = () => builder.Build(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
