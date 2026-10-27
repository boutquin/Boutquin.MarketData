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
    public void Rejects_null_required_factor_name_with_member_diagnostic()
    {
        var act = () => _sut.Normalize(default);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("raw")
            .WithMessage("*FactorName*");
    }

    private static readonly DateOnly s_date = new(2024, 1, 2);

    [Theory]
    [InlineData("Mkt-RF", 0.39, 0.0039)]
    [InlineData("SMB", -0.12, -0.0012)]
    [InlineData("HML", 0.05, 0.0005)]
    public void Converts_percent_factor_return_to_decimal(string name, double raw, double expected)
    {
        var obs = new FactorObservation(s_date, name, (decimal)raw);

        var result = _sut.Normalize(obs);

        result.Value.Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData("mkt-rf", "Mkt-RF")]
    [InlineData("smb", "SMB")]
    [InlineData("hml", "HML")]
    [InlineData("RMW", "RMW")]
    [InlineData("cma", "CMA")]
    [InlineData("mom", "Mom")]
    [InlineData("wml", "WML")]
    public void Standardizes_factor_name_to_canonical_casing(string raw, string canonical)
    {
        var obs = new FactorObservation(s_date, raw, 1.0m);

        var result = _sut.Normalize(obs);

        result.FactorName.Should().Be(canonical);
    }

    [Theory]
    [InlineData(" Mkt-RF ", "Mkt-RF")]
    [InlineData(" Mom", "Mom")]
    [InlineData("mkt-rf\t", "Mkt-RF")]
    [InlineData("\n smb ", "SMB")]
    public void Padded_known_factor_name_maps_to_the_same_canonical_name(string raw, string canonical)
    {
        // Whitespace must not select a different canonicalisation branch: FactorName is a persisted
        // dataset-key discriminant (FactorObservationDatasetKey Tag 4), so " Mkt-RF " canonicalising
        // to "MKT-RF" while "Mkt-RF" canonicalises to "Mkt-RF" forks one logical factor into two
        // persisted series.
        var obs = new FactorObservation(s_date, raw, 1.0m);

        var result = _sut.Normalize(obs);

        result.FactorName.Should().Be(canonical);
    }

    [Fact]
    public void Preserves_date()
    {
        var date = new DateOnly(2024, 6, 15);
        var obs = new FactorObservation(date, "RF", 0.02m);

        var result = _sut.Normalize(obs);

        result.Date.Should().Be(date);
    }

    [Fact]
    public void Unknown_factor_name_is_trimmed_and_uppercased()
    {
        var obs = new FactorObservation(s_date, "  custom_factor  ", 1.5m);

        var result = _sut.Normalize(obs);

        result.FactorName.Should().Be("CUSTOM_FACTOR");
        result.Value.Should().Be(0.015m);
    }

    [Fact]
    public void Zero_factor_return_stays_zero()
    {
        var obs = new FactorObservation(s_date, "Mkt-RF", 0m);

        var result = _sut.Normalize(obs);

        result.Value.Should().Be(0m);
    }

    [Fact]
    public void RF_factor_also_converted()
    {
        var obs = new FactorObservation(s_date, "RF", 0.02m);

        var result = _sut.Normalize(obs);

        result.FactorName.Should().Be("RF");
        result.Value.Should().Be(0.0002m);
    }
}
