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
using Boutquin.MarketData.Normalization.Filters;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Normalization.Tests.Unit;

public sealed class DateRangeFilterTests
{
    private static readonly List<KeyValuePair<DateOnly, decimal>> s_testData = new()
    {
        new(new DateOnly(2024, 1, 1), 100m),
        new(new DateOnly(2024, 3, 15), 200m),
        new(new DateOnly(2024, 6, 30), 300m),
        new(new DateOnly(2024, 9, 15), 400m),
        new(new DateOnly(2024, 12, 31), 500m),
    };

    [Fact]
    public void Apply_with_both_bounds_filters_inclusively()
    {
        var result = DateRangeFilter.Apply(
            s_testData,
            new DateOnly(2024, 3, 15),
            new DateOnly(2024, 9, 15)).ToList();

        result.Should().HaveCount(3);
        result.Select(r => r.Value).Should().BeEquivalentTo(new[] { 200m, 300m, 400m });
    }

    [Fact]
    public void Apply_with_null_from_returns_all_up_to_end()
    {
        var result = DateRangeFilter.Apply(
            s_testData,
            null,
            new DateOnly(2024, 6, 30)).ToList();

        result.Should().HaveCount(3);
        result.Select(r => r.Value).Should().BeEquivalentTo(new[] { 100m, 200m, 300m });
    }

    [Fact]
    public void Apply_with_null_to_returns_all_from_start()
    {
        var result = DateRangeFilter.Apply(
            s_testData,
            new DateOnly(2024, 6, 30),
            null).ToList();

        result.Should().HaveCount(3);
        result.Select(r => r.Value).Should().BeEquivalentTo(new[] { 300m, 400m, 500m });
    }

    [Fact]
    public void Apply_with_both_null_returns_all()
    {
        var result = DateRangeFilter.Apply(s_testData, null, null).ToList();

        result.Should().HaveCount(5);
    }

    [Fact]
    public void Apply_with_no_matches_returns_empty()
    {
        var result = DateRangeFilter.Apply(
            s_testData,
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 12, 31)).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_with_empty_source_returns_empty()
    {
        var result = DateRangeFilter.Apply(
            Enumerable.Empty<KeyValuePair<DateOnly, decimal>>(),
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31)).ToList();

        result.Should().BeEmpty();
    }

    [Fact]
    public void Apply_with_selector_filters_by_extracted_date()
    {
        var records = new[]
        {
            new TestRecord(new DateOnly(2024, 1, 1), "alpha"),
            new TestRecord(new DateOnly(2024, 6, 15), "beta"),
            new TestRecord(new DateOnly(2024, 12, 31), "gamma"),
        };

        var result = DateRangeFilter.Apply(
            records,
            r => r.Date,
            new DateOnly(2024, 3, 1),
            new DateOnly(2024, 9, 1)).ToList();

        result.Should().ContainSingle().Which.Name.Should().Be("beta");
    }

    private sealed record TestRecord(DateOnly Date, string Name);
}
