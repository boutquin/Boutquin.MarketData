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

using Boutquin.MarketData.Abstractions.Calendars;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Results;

public sealed class DataCoverageTests
{
    [Fact]
    public void Gap_classification_accepts_inclusive_maximum_date()
    {
        var range = new DateRange(DateOnly.MaxValue, DateOnly.MaxValue);
        var result = () => CoverageComputation.ComputeWithGaps(new EveryDayCalendar(), range, DataFrequency.Daily,
            new HashSet<DateOnly> { DateOnly.MaxValue });
        result.Should().NotThrow().Subject.Coverage.ReturnedPoints.Should().Be(1);
        CoverageComputation.ClassifyGaps(new EveryDayCalendar(), range, new HashSet<DateOnly> { DateOnly.MaxValue }).Should().BeEmpty();
    }

    [Fact]
    public void Default_calendar_enumeration_accepts_inclusive_maximum_date()
    {
        IBusinessCalendar calendar = new EveryDayCalendar();
        var enumerate = () => calendar.BusinessDaysBetween(DateOnly.MaxValue, DateOnly.MaxValue);
        enumerate.Should().NotThrow().Subject.Should().Equal(DateOnly.MaxValue);
    }

    [Fact]
    public void Outside_returned_date_cannot_certify_missing_requested_day()
    {
        var day = new DateOnly(2026, 1, 5);
        var result = CoverageComputation.ComputeWithGaps(new EveryDayCalendar(), new DateRange(day, day), DataFrequency.Daily,
            new HashSet<DateOnly> { day.AddDays(1) });
        result.Coverage.Status.Should().Be(CompletenessStatus.Missing);
        result.Coverage.ReturnedPoints.Should().Be(0);
        result.Coverage.MissingPoints.Should().Be(1);
        result.GapIssues.Should().ContainSingle();
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(int.MaxValue, 1, 0)]
    public void Compute_bounds_ratio_without_discarding_actual_rows(int returned, int expectedRatio, int missing)
    {
        var day = new DateOnly(2025, 1, 6);
        var coverage = CoverageComputation.Compute(new EveryDayCalendar(), new DateRange(day, day), DataFrequency.Daily, returned);
        coverage.ReturnedPoints.Should().Be(returned);
        coverage.CoverageRatio.Should().Be(expectedRatio);
        coverage.MissingPoints.Should().Be(missing);
        coverage.Status.Should().Be(returned == 0 ? CompletenessStatus.Missing : CompletenessStatus.Complete);
    }

    [Fact]
    public void Compute_rejects_negative_returned_count()
    {
        var day = new DateOnly(2025, 1, 6);
        var compute = () => CoverageComputation.Compute(new EveryDayCalendar(), new DateRange(day, day), DataFrequency.Daily, -1);
        compute.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("returnedPoints");
    }

    [Fact]
    public void Weekly_bucket_surplus_is_complete_and_preserves_observation_count()
    {
        var monday = new DateOnly(2025, 1, 6);
        var coverage = CoverageComputation.ComputeWithGaps(new EveryDayCalendar(), new DateRange(monday, monday.AddDays(1)),
            DataFrequency.Weekly, new HashSet<DateOnly> { monday, monday.AddDays(1) }).Coverage;
        coverage.RequestedPoints.Should().Be(1);
        coverage.ReturnedPoints.Should().Be(2);
        coverage.CoverageRatio.Should().Be(1m);
        coverage.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Aggregate_recomputes_bounded_ratio_even_for_one_partition(int partitions)
    {
        var inputs = Enumerable.Repeat(new DataCoverage(1, 2, 0, 2m), partitions).ToArray();
        var result = CoverageComputation.Aggregate(inputs);
        result.RequestedPoints.Should().Be(partitions);
        result.ReturnedPoints.Should().Be(2 * partitions);
        result.MissingPoints.Should().Be(0);
        result.CoverageRatio.Should().Be(1m);
        result.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void Aggregate_rejects_negative_counts_even_for_one_partition(int requested, int returned, int missing)
    {
        var coverage = new DataCoverage(requested, returned, missing, 1m);
        var single = () => CoverageComputation.Aggregate(new[] { coverage });
        var multiple = () => CoverageComputation.Aggregate(new[] { new DataCoverage(1, 1, 0, 1m), coverage });
        single.Should().Throw<ArgumentOutOfRangeException>();
        multiple.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(int.MaxValue, 0, 1, 0)]
    [InlineData(0, int.MaxValue, 0, 1)]
    public void Aggregate_still_rejects_unrepresentable_totals(int requestedA, int returnedA, int requestedB, int returnedB)
    {
        var aggregate = () => CoverageComputation.Aggregate(new[]
        {
            new DataCoverage(requestedA, returnedA, 0, 1m),
            new DataCoverage(requestedB, returnedB, 0, 1m),
        });
        aggregate.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Aggregate_zero_requested_points_is_complete_and_preserves_returned_count()
    {
        var result = CoverageComputation.Aggregate(new[] { new DataCoverage(0, 3, 0, 1m) });
        result.RequestedPoints.Should().Be(0);
        result.ReturnedPoints.Should().Be(3);
        result.CoverageRatio.Should().Be(1m);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    public void Aggregate_preserves_certified_known_empty_units(int partitions, int returnedPerPartition)
    {
        // Sparse dividends may have no payment on a date the source has completely checked.
        var inputs = Enumerable.Repeat(new DataCoverage(2, returnedPerPartition, 0, 1m), partitions).ToArray();
        var result = CoverageComputation.Aggregate(inputs);
        result.RequestedPoints.Should().Be(2 * partitions);
        result.ReturnedPoints.Should().Be(returnedPerPartition * partitions);
        result.MissingPoints.Should().Be(0);
        result.CoverageRatio.Should().Be(1m);
    }

    [Fact]
    public void Aggregate_certified_empty_partition_does_not_certify_failed_sibling()
    {
        var result = CoverageComputation.Aggregate(new[]
        {
            new DataCoverage(2, 0, 0, 1m),
            new DataCoverage(1, 0, 1, 0m),
        });
        result.RequestedPoints.Should().Be(3);
        result.ReturnedPoints.Should().Be(0);
        result.MissingPoints.Should().Be(1);
        result.CoverageRatio.Should().Be(2m / 3m);
        result.Status.Should().Be(CompletenessStatus.Partial);
    }

    [Fact]
    public void Aggregate_certified_empty_and_partial_partitions_weight_logical_coverage()
    {
        var result = CoverageComputation.Aggregate(new[]
        {
            new DataCoverage(2, 0, 0, 1m),
            new DataCoverage(2, 1, 1, 0.5m),
        });
        result.RequestedPoints.Should().Be(4);
        result.ReturnedPoints.Should().Be(1);
        result.MissingPoints.Should().Be(1);
        result.CoverageRatio.Should().Be(0.75m);
    }

    [Fact]
    public void Aggregate_surplus_does_not_cover_sibling_gap()
    {
        var result = CoverageComputation.Aggregate(new[]
        {
            new DataCoverage(1, 3, 0, 1m),
            new DataCoverage(1, 0, 1, 0m),
        });
        result.ReturnedPoints.Should().Be(3);
        result.MissingPoints.Should().Be(1);
        result.CoverageRatio.Should().Be(0.5m);
        result.Status.Should().Be(CompletenessStatus.Partial);
    }

    [Fact]
    public void Aggregate_partial_response_with_zero_missing_does_not_certify_absence()
    {
        var result = CoverageComputation.Aggregate(new[] { new DataCoverage(2, 1, 0, 0.5m) });
        result.ReturnedPoints.Should().Be(1);
        result.MissingPoints.Should().Be(1);
        result.CoverageRatio.Should().Be(0.5m);
    }

    private sealed class EveryDayCalendar : IBusinessCalendar
    {
        public string Code => "ALL";
        public int BusinessDaysPerYear => 365;
        public bool IsBusinessDay(DateOnly date) => true;
        public DateOnly Adjust(DateOnly date, BusinessDayAdjustment adjustment) => date;
        public DateOnly Advance(DateOnly date, int businessDays) => date.AddDays(businessDays);
    }

    [Fact]
    public void Status_ReturnsComplete_WhenRatioIs1()
    {
        // Arrange
        var coverage = new DataCoverage(100, 100, 0, 1.0m);

        // Act & Assert
        coverage.Status.Should().Be(CompletenessStatus.Complete);
    }

    [Fact]
    public void Status_ReturnsMissing_WhenRatioIs0()
    {
        // Arrange
        var coverage = new DataCoverage(100, 0, 100, 0.0m);

        // Act & Assert
        coverage.Status.Should().Be(CompletenessStatus.Missing);
    }

    [Fact]
    public void Status_ReturnsPartial_WhenBetween0And1()
    {
        // Arrange
        var coverage = new DataCoverage(100, 75, 25, 0.75m);

        // Act & Assert
        coverage.Status.Should().Be(CompletenessStatus.Partial);
    }

    [Fact]
    public void CoverageRatio_Computation()
    {
        // Arrange & Act
        var coverage = new DataCoverage(
            RequestedPoints: 200,
            ReturnedPoints: 150,
            MissingPoints: 50,
            CoverageRatio: 0.75m);

        // Assert
        coverage.RequestedPoints.Should().Be(200);
        coverage.ReturnedPoints.Should().Be(150);
        coverage.MissingPoints.Should().Be(50);
        coverage.CoverageRatio.Should().Be(0.75m);
    }
}
