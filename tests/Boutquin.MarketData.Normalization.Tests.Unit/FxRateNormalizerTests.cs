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

using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Normalization.Normalizers;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Normalization.Tests.Unit;

public sealed class FxRateNormalizerTests
{
    private readonly FxRateNormalizer _sut = new();

    [Fact]
    public void Default_record_is_valid_under_required_string_policy()
    {
        var result = _sut.Normalize(default);

        result.Should().Be(default(FxRate));
    }

    [Fact]
    public void Rounds_rate_to_configured_precision()
    {
        // FxRate slimmed to (Date, Rate) per M-2.6 (§2.5.1 / Q15) — the pair lives in
        // FxRateDatasetKey, so the normalizer no longer carries currencies on the body.
        var rate = new FxRate(new DateOnly(2024, 1, 1), 1.08501234567m);

        var result = _sut.Normalize(rate);

        // Default 6 decimal places.
        result.Rate.Should().Be(1.085012m);
    }

    [Fact]
    public void Preserves_date()
    {
        var date = new DateOnly(2024, 6, 15);
        var rate = new FxRate(date, 198.45m);

        var result = _sut.Normalize(rate);

        result.Date.Should().Be(date);
    }

    [Fact]
    public void Exact_rate_unchanged()
    {
        var rate = new FxRate(new DateOnly(2024, 1, 1), 1.3500m);

        var result = _sut.Normalize(rate);

        result.Rate.Should().Be(1.3500m);
    }

    [Fact]
    public void Custom_precision_rounds_to_specified_decimals()
    {
        var normalizer = new FxRateNormalizer(decimalPlaces: 4);
        var rate = new FxRate(new DateOnly(2024, 1, 1), 1.08505m);

        var result = normalizer.Normalize(rate);

        result.Rate.Should().Be(1.0851m);
    }
}
