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

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>
/// Represents an inclusive date range defined by a start and end date.
/// </summary>
/// <remarks>
/// Both boundaries are inclusive. <c>From</c> must be on or before <c>To</c>;
/// the constructor throws <see cref="ArgumentOutOfRangeException"/> for inverted
/// ranges because every downstream count (expected points, business days, weeks)
/// would silently return zero, masking caller bugs. The range is calendar-based
/// (not trading-day-based); adapters handle business-day alignment internally.
/// </remarks>
public sealed record DateRange
{
    /// <summary>Initializes a new <see cref="DateRange"/>.</summary>
    /// <param name="from">The start date (inclusive).</param>
    /// <param name="to">The end date (inclusive). Must be on or after <paramref name="from"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="to"/> is earlier than <paramref name="from"/>.
    /// </exception>
    public DateRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentOutOfRangeException(
                nameof(to),
                $"to ({to:O}) must be on or after from ({from:O}).");
        }

        From = from;
        To = to;
    }

    /// <summary>The start date of the range (inclusive).</summary>
    public DateOnly From { get; init; }

    /// <summary>The end date of the range (inclusive).</summary>
    public DateOnly To { get; init; }

    /// <summary>Deconstructs into a (From, To) tuple.</summary>
    public void Deconstruct(out DateOnly from, out DateOnly to)
    {
        from = From;
        to = To;
    }
}
