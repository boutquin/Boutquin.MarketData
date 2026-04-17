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

public sealed class FactorObservationNormalizerTests
{
    private readonly FactorObservationNormalizer _sut = new();

    [Fact]
    public void Converts_percent_factor_returns_to_decimal()
    {
        var factors = new Dictionary<string, decimal>
        {
            ["Mkt-RF"] = 0.39m,
            ["SMB"] = -0.12m,
            ["HML"] = 0.05m,
        };
        var obs = new FactorObservation(new DateOnly(2024, 1, 2), factors);

        var result = _sut.Normalize(obs);

        result.Factors["Mkt-RF"].Should().Be(0.0039m);
        result.Factors["SMB"].Should().Be(-0.0012m);
        result.Factors["HML"].Should().Be(0.0005m);
    }

    [Fact]
    public void Standardizes_factor_names()
    {
        var factors = new Dictionary<string, decimal>
        {
            ["mkt-rf"] = 1.0m,
            ["smb"] = 0.5m,
            ["hml"] = -0.3m,
        };
        var obs = new FactorObservation(new DateOnly(2024, 1, 2), factors);

        var result = _sut.Normalize(obs);

        result.Factors.Keys.Should().Contain("Mkt-RF");
        result.Factors.Keys.Should().Contain("SMB");
        result.Factors.Keys.Should().Contain("HML");
    }

    [Fact]
    public void Preserves_date()
    {
        var date = new DateOnly(2024, 6, 15);
        var obs = new FactorObservation(date, new Dictionary<string, decimal> { ["RF"] = 0.02m });

        var result = _sut.Normalize(obs);

        result.Date.Should().Be(date);
    }

    [Fact]
    public void Unknown_factor_names_uppercased()
    {
        var factors = new Dictionary<string, decimal> { ["custom_factor"] = 1.5m };
        var obs = new FactorObservation(new DateOnly(2024, 1, 2), factors);

        var result = _sut.Normalize(obs);

        result.Factors.Keys.Should().Contain("CUSTOM_FACTOR");
        result.Factors["CUSTOM_FACTOR"].Should().Be(0.015m);
    }

    [Fact]
    public void Zero_factor_return_stays_zero()
    {
        var factors = new Dictionary<string, decimal> { ["Mkt-RF"] = 0m };
        var obs = new FactorObservation(new DateOnly(2024, 1, 2), factors);

        var result = _sut.Normalize(obs);

        result.Factors["Mkt-RF"].Should().Be(0m);
    }

    [Fact]
    public void RF_factor_also_converted()
    {
        var factors = new Dictionary<string, decimal> { ["RF"] = 0.02m };
        var obs = new FactorObservation(new DateOnly(2024, 1, 2), factors);

        var result = _sut.Normalize(obs);

        result.Factors["RF"].Should().Be(0.0002m);
    }

    [Fact]
    public void Empty_factors_produces_empty_result()
    {
        var obs = new FactorObservation(new DateOnly(2024, 1, 2), new Dictionary<string, decimal>());

        var result = _sut.Normalize(obs);

        result.Factors.Should().BeEmpty();
    }
}
