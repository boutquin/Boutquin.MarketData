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

namespace Boutquin.MarketData.Orchestration.Coverage;

/// <summary>
/// DI-friendly wrapper over <see cref="CoverageComputation"/> that binds a default
/// <see cref="IBusinessCalendar"/> at construction time so pipeline callers can compute
/// coverage without plumbing a calendar through every request.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline role: called by <c>DataPipeline</c> when the adapter chain did not itself report
/// coverage (for example synthetic or record-only adapters). Adapter-reported coverage is
/// preferred when available because the adapter knows which source-specific calendar applies.
/// </para>
/// <para>
/// Split between instance and static members: the instance <see cref="Compute(DateRange, DataFrequency, int)"/>
/// uses the calendar captured at construction and is intended for DI consumers. The static
/// overloads let callers supply a different calendar per-call (for example when a partitioner
/// knows the dataset is FX rather than US equity) without re-registering the service.
/// </para>
/// <para>
/// The canonical counting and gap-classification logic lives in
/// <see cref="CoverageComputation"/> (in Abstractions) so that both the Orchestration
/// pipeline and Adapter packages can share it without creating a circular dependency.
/// </para>
/// </remarks>
public sealed class CoverageCalculator
{
    private readonly IBusinessCalendar _calendar;

    /// <summary>
    /// Captures the default business calendar used by the instance-level
    /// <see cref="Compute(DateRange, DataFrequency, int)"/> overload.
    /// </summary>
    /// <param name="calendar">Calendar that defines which dates in a range are expected trading days. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="calendar"/> is <see langword="null"/>.</exception>
    public CoverageCalculator(IBusinessCalendar calendar)
    {
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
    }

    /// <summary>
    /// Computes coverage for a request using the calendar captured at construction. Preferred
    /// entry point for DI consumers whose dataset shares a single calendar.
    /// </summary>
    /// <param name="range">Requested date range (inclusive bounds, interpreted in the calendar's timezone).</param>
    /// <param name="frequency">Granularity the expected-points count should be derived from (daily, weekly, etc.).</param>
    /// <param name="returnedPoints">Number of records the adapter actually returned for the range.</param>
    /// <returns>Coverage ratio, expected/returned counts, and completeness classification.</returns>
    public DataCoverage Compute(DateRange range, DataFrequency frequency, int returnedPoints) =>
        CoverageComputation.Compute(_calendar, range, frequency, returnedPoints);

    /// <summary>
    /// Computes coverage against a caller-supplied calendar. Use when the dataset's calendar
    /// differs from the pipeline's default (for example FX vs. US equity) and you do not want
    /// to re-register the DI service.
    /// </summary>
    /// <param name="calendar">Calendar that defines expected trading days for this specific call.</param>
    /// <param name="range">Requested date range.</param>
    /// <param name="frequency">Granularity the expected-points count should be derived from.</param>
    /// <param name="returnedPoints">Number of records the adapter actually returned.</param>
    /// <returns>Coverage ratio, expected/returned counts, and completeness classification.</returns>
    public static DataCoverage Compute(
        IBusinessCalendar calendar, DateRange range, DataFrequency frequency, int returnedPoints) =>
        CoverageComputation.Compute(calendar, range, frequency, returnedPoints);

    /// <summary>
    /// Computes calendar-aware coverage and classifies each missing date as a
    /// <c>MISSING_DATES</c> <see cref="DataIssue"/>. Use when consumers need to know which
    /// specific dates were absent, not just the aggregate ratio.
    /// </summary>
    /// <param name="calendar">Calendar that defines expected trading days.</param>
    /// <param name="range">Requested date range.</param>
    /// <param name="frequency">Granularity the expected-dates set should be derived from.</param>
    /// <param name="returnedDates">Set of dates present in the adapter response, used to diff against expected.</param>
    /// <returns>Tuple of (aggregate coverage, per-date gap issues). The issues list is empty when coverage is complete.</returns>
    public static (DataCoverage Coverage, IReadOnlyList<DataIssue> GapIssues) ComputeWithGaps(
        IBusinessCalendar calendar,
        DateRange range,
        DataFrequency frequency,
        IReadOnlySet<DateOnly> returnedDates) =>
        CoverageComputation.ComputeWithGaps(calendar, range, frequency, returnedDates);

    /// <summary>
    /// Combines per-partition coverage values into a single aggregate. Used by the pipeline
    /// after a batch completes so the final envelope reports overall coverage rather than a
    /// list of per-partition snippets.
    /// </summary>
    /// <param name="coverages">Coverage results from each partition, in any order.</param>
    /// <returns>Combined coverage: summed expected and returned counts with a recomputed ratio.</returns>
    public static DataCoverage Aggregate(IReadOnlyList<DataCoverage> coverages) =>
        CoverageComputation.Aggregate(coverages);
}
