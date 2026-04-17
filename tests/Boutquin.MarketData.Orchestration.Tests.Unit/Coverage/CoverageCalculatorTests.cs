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

using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Calendars;
using Boutquin.MarketData.Orchestration.Coverage;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Orchestration.Tests.Unit.Coverage;

public sealed class CoverageCalculatorTests
{
    private readonly CoverageCalculator _sut = new(new WeekendOnlyCalendar());

    [Fact]
    public void Daily_counts_business_days_only()
    {
        // Mon Jan 1 2024 through Fri Jan 5 2024 = 5 business days
        var range = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5));

        var result = _sut.Compute(range, DataFrequency.Daily, 5);

        result.RequestedPoints.Should().Be(5);
        result.ReturnedPoints.Should().Be(5);
        result.CoverageRatio.Should().Be(1.0m);
        result.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Fact]
    public void Daily_skips_weekends()
    {
        // Mon Jan 1 2024 through Sun Jan 7 2024 = 5 biz days (Sat+Sun skipped)
        var range = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 7));

        var result = _sut.Compute(range, DataFrequency.Daily, 3);

        result.RequestedPoints.Should().Be(5);
        result.ReturnedPoints.Should().Be(3);
        result.MissingPoints.Should().Be(2);
        result.Status.Should().Be(CompletenessStatus.Partial);
    }

    [Fact]
    public void Weekly_counts_weeks()
    {
        // Jan 1 to Jan 28 2024 = 4 weeks
        var range = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 28));

        var result = _sut.Compute(range, DataFrequency.Weekly, 4);

        result.RequestedPoints.Should().Be(4);
        result.CoverageRatio.Should().Be(1.0m);
    }

    [Fact]
    public void Monthly_counts_months()
    {
        // Jan to Dec 2024 = 12 months
        var range = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        var result = _sut.Compute(range, DataFrequency.Monthly, 12);

        result.RequestedPoints.Should().Be(12);
        result.CoverageRatio.Should().Be(1.0m);
    }

    [Fact]
    public void Zero_returned_points_yields_Missing()
    {
        var range = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5));

        var result = _sut.Compute(range, DataFrequency.Daily, 0);

        result.ReturnedPoints.Should().Be(0);
        result.CoverageRatio.Should().Be(0.0m);
        result.Status.Should().Be(CompletenessStatus.Missing);
    }

    [Fact]
    public void All_points_returned_yields_Complete()
    {
        // Mon-Fri = 5 biz days
        var range = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5));

        var result = _sut.Compute(range, DataFrequency.Daily, 5);

        result.CoverageRatio.Should().Be(1.0m);
        result.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Fact]
    public void Partial_coverage_computes_correct_ratio()
    {
        // 5 biz days, 3 returned → 60%
        var range = new DateRange(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 5));

        var result = _sut.Compute(range, DataFrequency.Daily, 3);

        result.CoverageRatio.Should().Be(0.6m);
        result.Status.Should().Be(CompletenessStatus.Partial);
    }
}
