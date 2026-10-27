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
using Boutquin.MarketData.Abstractions.Diagnostics;
using Boutquin.MarketData.Abstractions.Requests;

namespace Boutquin.MarketData.Abstractions.Results;

/// <summary>
/// Canonical static methods for computing <see cref="DataCoverage"/> and classifying date gaps.
/// Shared by both the pipeline's <c>CoverageCalculator</c> and adapter-level coverage helpers,
/// ensuring a single source of truth for counting logic.
/// </summary>
public static class CoverageComputation
{
    /// <summary>
    /// Computes coverage using a specific calendar, preserving the actual returned count
    /// while bounding the completeness ratio at one for surplus observations.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The returned point count is negative.</exception>
    public static DataCoverage Compute(
        IBusinessCalendar calendar, DateRange range, DataFrequency frequency, int returnedPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(returnedPoints);

        var requested = CountExpectedPoints(calendar, range, frequency);
        var missing = Math.Max(0, requested - returnedPoints);
        var ratio = requested == 0 ? 1.0m : Math.Min(1.0m, (decimal)returnedPoints / requested);
        return new DataCoverage(requested, returnedPoints, missing, ratio);
    }

    /// <summary>
    /// Computes calendar-aware coverage and classifies any date gaps for daily frequency data.
    /// </summary>
    /// <param name="calendar">The business calendar governing publication days for the data source.</param>
    /// <param name="range">The requested date range (inclusive).</param>
    /// <param name="frequency">The data sampling frequency.</param>
    /// <param name="returnedDates">
    /// The set of dates for which data was actually returned. Dates outside the requested range,
    /// and non-business dates at daily frequency, do not contribute to coverage.
    /// </param>
    /// <returns>A tuple of the computed coverage and any gap-related issues.</returns>
    public static (DataCoverage Coverage, IReadOnlyList<DataIssue> GapIssues) ComputeWithGaps(
        IBusinessCalendar calendar,
        DateRange range,
        DataFrequency frequency,
        IReadOnlySet<DateOnly> returnedDates)
    {
        var eligibleCount = returnedDates.Count(date => date >= range.From && date <= range.To
            && (frequency != DataFrequency.Daily || calendar.IsBusinessDay(date)));
        var coverage = Compute(calendar, range, frequency, eligibleCount);

        var gapIssues = frequency == DataFrequency.Daily
            ? ClassifyGaps(calendar, range, returnedDates)
            : Array.Empty<DataIssue>();

        return (coverage, gapIssues);
    }

    /// <summary>
    /// Aggregates multiple per-partition coverages into a single combined coverage.
    /// </summary>
    /// <remarks>
    /// Accumulators are computed in a single pass using <see cref="long"/> to avoid
    /// <see cref="int"/> overflow when many partitions × large date ranges push totals past
    /// <see cref="int.MaxValue"/>. A partition with complete coverage and zero missing points
    /// certifies its whole requested range, including known-empty dates in sparse series.
    /// Other partitions contribute at most their requested count from their returned observations.
    /// Missing points and the ratio are recomputed from those covered units, so a surplus in one
    /// partition cannot conceal a gap in another. Actual returned counts are preserved separately.
    /// Counts must be non-negative, and the same rules apply to a single partition.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A partition has a negative point count.</exception>
    /// <exception cref="OverflowException">
    /// Thrown when the aggregated total exceeds <see cref="int.MaxValue"/>, which indicates the
    /// coverage sum is no longer representable in the canonical <see cref="int"/>-valued fields.
    /// </exception>
    public static DataCoverage Aggregate(IReadOnlyList<DataCoverage> coverages)
    {
        if (coverages.Count == 0)
        {
            return new DataCoverage(0, 0, 0, 1.0m);
        }

        long totalRequested = 0;
        long totalReturned = 0;
        long totalCovered = 0;
        for (var i = 0; i < coverages.Count; i++)
        {
            var coverage = coverages[i];
            if (coverage.RequestedPoints < 0 || coverage.ReturnedPoints < 0 || coverage.MissingPoints < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(coverages), "Coverage point counts must be non-negative.");
            }

            totalRequested += coverage.RequestedPoints;
            totalReturned += coverage.ReturnedPoints;
            // Source-certified empty dates are covered even though they produce no records.
            // Keep that certificate within its own partition, as with returned observations.
            totalCovered += coverage.Status == CompletenessStatus.Complete && coverage.MissingPoints == 0
                ? coverage.RequestedPoints
                : Math.Min(coverage.RequestedPoints, coverage.ReturnedPoints);
        }

        var totalMissing = totalRequested - totalCovered;
        var ratio = totalRequested == 0 ? 1.0m : (decimal)totalCovered / totalRequested;

        return new DataCoverage(
            checked((int)totalRequested),
            checked((int)totalReturned),
            checked((int)totalMissing),
            ratio);
    }

    /// <summary>
    /// Counts the expected number of data points for the given range and frequency.
    /// </summary>
    public static int CountExpectedPoints(
        IBusinessCalendar calendar, DateRange range, DataFrequency frequency) =>
        frequency switch
        {
            DataFrequency.Daily => CountBusinessDays(calendar, range.From, range.To),
            DataFrequency.Weekly => CountWeeks(range.From, range.To),
            DataFrequency.Monthly => CountMonths(range.From, range.To),
            _ => CountCalendarDays(range.From, range.To),
        };

    /// <summary>
    /// Counts business days in the inclusive range [from, to] using the given calendar.
    /// </summary>
    public static int CountBusinessDays(IBusinessCalendar calendar, DateOnly from, DateOnly to)
    {
        // Fast-path: sentinel full-universe range used by point-in-time requests such as
        // InstrumentMetadataRequest. Avoids iterating ~3.65 million days and prevents
        // DateOnly.MaxValue.AddDays(1) overflow at the loop boundary.
        if (from == DateOnly.MinValue && to == DateOnly.MaxValue)
        {
            return 0;
        }

        var count = 0;
        var current = from;
        while (current <= to)
        {
            if (calendar.IsBusinessDay(current))
            {
                count++;
            }

            if (current == to)
            {
                break;
            }

            current = current.AddDays(1);
        }

        return count;
    }

    /// <summary>
    /// Classifies missing business-day dates as unexpected gaps.
    /// Non-business-day gaps are silently ignored — they are expected omissions.
    /// </summary>
    public static IReadOnlyList<DataIssue> ClassifyGaps(
        IBusinessCalendar calendar,
        DateRange range,
        IReadOnlySet<DateOnly> returnedDates)
    {
        var issues = new List<DataIssue>();
        var current = range.From;

        while (current <= range.To)
        {
            if (calendar.IsBusinessDay(current) && !returnedDates.Contains(current))
            {
                issues.Add(new DataIssue(
                    IssueCode.UnexpectedGap,
                    IssueSeverity.Warning,
                    $"Expected data for business day {current:yyyy-MM-dd} but none was returned."));
            }

            if (current == range.To)
            {
                break;
            }

            current = current.AddDays(1);
        }

        return issues;
    }

    private static int CountWeeks(DateOnly from, DateOnly to)
    {
        var days = to.DayNumber - from.DayNumber;
        return days < 0 ? 0 : (days / 7) + 1;
    }

    private static int CountMonths(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return 0;
        }

        return ((to.Year - from.Year) * 12) + (to.Month - from.Month) + 1;
    }

    private static int CountCalendarDays(DateOnly from, DateOnly to)
    {
        var days = to.DayNumber - from.DayNumber;
        return days < 0 ? 0 : days + 1;
    }
}
