// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

using System.Globalization;
using System.Text.Json.Serialization;

namespace Boutquin.MarketData.Abstractions.ReferenceData;

/// <summary>
/// Year/month pair representing the delivery month of a futures contract
/// (canonical rendering <c>"YYYY-MM"</c>).
/// </summary>
/// <remarks>
/// <para>
/// Replaces the bare-<c>string</c> <c>ContractMonth</c> carried on
/// <see cref="Records.FuturesSettlement"/>. Futures delivery months have well-defined
/// calendar semantics, so the value is stored as a <c>(Year, Month)</c> tuple rather
/// than a string. The canonical string form is ISO-ordered <c>YYYY-MM</c>; the
/// compact form <c>YYYYMM</c> used by some exchange feeds is also accepted by the
/// string constructor.
/// </para>
/// <para>
/// Comparison is lexicographic over <c>(Year, Month)</c> so that
/// <see cref="SortedSet{T}"/>-keyed collections produce chronologically correct
/// ordering without needing <see cref="DateOnly"/>.
/// </para>
/// </remarks>
public readonly record struct ContractMonth : IComparable<ContractMonth>
{
    /// <summary>
    /// The calendar year (e.g. 2026).
    /// </summary>
    public int Year { get; }

    /// <summary>
    /// The calendar month, 1 (January) through 12 (December).
    /// </summary>
    public int Month { get; }

    /// <summary>
    /// Initializes a new <see cref="ContractMonth"/> from explicit year and month.
    /// </summary>
    /// <param name="year">Calendar year. Must be at least 1 and at most 9999.</param>
    /// <param name="month">Calendar month, 1–12.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="year"/> is outside <c>[1, 9999]</c> or
    /// <paramref name="month"/> is outside <c>[1, 12]</c>.
    /// </exception>
    [JsonConstructor]
    public ContractMonth(int year, int month)
    {
        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), year, "Year must be in [1, 9999].");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be in [1, 12].");
        }

        Year = year;
        Month = month;
    }

    /// <summary>
    /// Initializes a new <see cref="ContractMonth"/> by parsing a textual
    /// representation.
    /// </summary>
    /// <param name="text">
    /// Canonical <c>YYYY-MM</c> (e.g. <c>"2026-12"</c>) or compact <c>YYYYMM</c>
    /// (e.g. <c>"202612"</c>). Surrounding whitespace is trimmed.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="text"/> is <see langword="null"/>, whitespace, or
    /// does not match either accepted shape.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the parsed month is outside <c>[1, 12]</c> or the parsed year is
    /// outside <c>[1, 9999]</c>.
    /// </exception>
    public ContractMonth(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var trimmed = text.Trim();

        int year;
        int month;

        // Canonical "YYYY-MM"
        if (trimmed.Length == 7 && trimmed[4] == '-'
            && int.TryParse(trimmed.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && int.TryParse(trimmed.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month))
        {
            // fall through to validation
        }
        // Compact "YYYYMM"
        else if (trimmed.Length == 6
            && int.TryParse(trimmed.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && int.TryParse(trimmed.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month))
        {
            // fall through to validation
        }
        else
        {
            throw new ArgumentException(
                $"Contract month '{text}' is not in 'YYYY-MM' or 'YYYYMM' form.",
                nameof(text));
        }

        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(text), year, "Year must be in [1, 9999].");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(text), month, "Month must be in [1, 12].");
        }

        Year = year;
        Month = month;
    }

    /// <summary>
    /// Compares two contract months chronologically by year then month.
    /// </summary>
    /// <param name="other">The contract month to compare against.</param>
    /// <returns>
    /// Negative if this contract month precedes <paramref name="other"/>; zero if
    /// equal; positive if this contract month follows.
    /// </returns>
    public int CompareTo(ContractMonth other)
    {
        var yearCmp = Year.CompareTo(other.Year);
        return yearCmp != 0 ? yearCmp : Month.CompareTo(other.Month);
    }

    /// <summary>
    /// Returns the first day of the contract month as a <see cref="DateOnly"/>
    /// for interop with date-based collections.
    /// </summary>
    /// <returns>The first day of <see cref="Year"/>/<see cref="Month"/>.</returns>
    public DateOnly ToFirstOfMonth() => new(Year, Month, 1);

    /// <summary>
    /// Implicitly converts a <see cref="ContractMonth"/> to <see cref="DateOnly"/>
    /// using the first day of the month, for sort ergonomics.
    /// </summary>
    /// <param name="month">The contract month.</param>
    public static implicit operator DateOnly(ContractMonth month) => month.ToFirstOfMonth();

    /// <summary>
    /// Returns the canonical <c>YYYY-MM</c> representation.
    /// </summary>
    /// <returns>The canonical rendering; for <c>default</c> returns <c>"0000-00"</c>.</returns>
    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
