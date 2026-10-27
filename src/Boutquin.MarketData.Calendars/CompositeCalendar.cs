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

namespace Boutquin.MarketData.Calendars;

/// <summary>
/// Combines multiple <see cref="IBusinessCalendar"/> instances into a single calendar whose
/// business days are defined by the intersection or union of its constituents.
/// </summary>
/// <remarks>
/// <para>
/// Pipeline role: used where an instrument or settlement cycle must respect more than one
/// jurisdiction's holidays. Examples: a cross-currency swap settling in USD and JPY needs
/// both settlement calendars open before it can settle; a dual-listed equity with market-makers in
/// NY and Toronto is tradeable if either venue is open.
/// </para>
/// <para>
/// Mode semantics:
/// <list type="bullet">
///   <item><description>
///   <see cref="CalendarCompositionMode.All"/> (intersection) — a date is a business day only
///   when every constituent calendar agrees. Correct default for settlement, because cash
///   cannot move on a jurisdiction-closed day even if the counterparty jurisdiction is open.
///   </description></item>
///   <item><description>
///   <see cref="CalendarCompositionMode.Any"/> (union) — a date is a business day when at
///   least one constituent is open. Appropriate for trading calendars where a venue being
///   open is sufficient to produce price observations.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <see cref="BusinessDaysPerYear"/> under composition: <see cref="CalendarCompositionMode.All"/>
/// returns the minimum across constituents (fewer shared business days than any single
/// calendar) and <see cref="CalendarCompositionMode.Any"/> returns the maximum (at least as
/// many business days as the most-open constituent). These are bounds, not exact counts —
/// callers that need precise counts should enumerate <see cref="IsBusinessDay"/> over the
/// range of interest.
/// </para>
/// </remarks>
public sealed class CompositeCalendar : BusinessCalendarBase
{
    private readonly IBusinessCalendar[] _calendars;
    private readonly CalendarCompositionMode _mode;

    /// <summary>
    /// Initializes a composite calendar in <see cref="CalendarCompositionMode.All"/>
    /// (intersection) mode. This is the default because settlement-style workflows — the
    /// most common consumer — require every jurisdiction to be open.
    /// </summary>
    /// <param name="code">Market code identifying this composite calendar (for example <c>US-CA</c> for joint US/CA settlement).</param>
    /// <param name="calendars">Constituent calendars whose intersection defines business days. At least one is required.</param>
    /// <exception cref="ArgumentNullException">The code or calendar array is null.</exception>
    /// <exception cref="ArgumentException">The calendar array is empty or contains null.</exception>
    public CompositeCalendar(string code, params IBusinessCalendar[] calendars)
        : this(code, CalendarCompositionMode.All, calendars)
    {
    }

    /// <summary>
    /// Initializes a composite calendar with an explicit composition mode. Use the
    /// <see cref="CalendarCompositionMode.Any"/> overload for trading calendars; use
    /// <see cref="CalendarCompositionMode.All"/> for settlement calendars.
    /// </summary>
    /// <param name="code">Market code identifying this composite calendar.</param>
    /// <param name="mode">Combination rule: intersection or union of constituent calendars.</param>
    /// <param name="calendars">Constituent calendars. At least one is required.</param>
    /// <exception cref="ArgumentNullException">The code or calendar array is null.</exception>
    /// <exception cref="ArgumentException">The calendar array is empty or contains null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The composition mode is undefined.</exception>
    public CompositeCalendar(string code, CalendarCompositionMode mode, params IBusinessCalendar[] calendars)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(calendars);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "A declared calendar composition mode is required.");
        }

        if (calendars.Any(calendar => calendar is null))
        {
            throw new ArgumentException("Constituent calendars must not contain null.", nameof(calendars));
        }

        if (calendars.Length == 0)
        {
            throw new ArgumentException("At least one calendar must be provided.", nameof(calendars));
        }

        Code = code;
        _mode = mode;
        _calendars = (IBusinessCalendar[])calendars.Clone();
    }

    /// <inheritdoc />
    public override string Code { get; }

    /// <inheritdoc />
    public override int BusinessDaysPerYear => _mode switch
    {
        // Union has at least as many business days as the most-open constituent, so Max is
        // a lower-bound annualization estimate without enumerating the year.
        CalendarCompositionMode.Any => _calendars.Max(c => c.BusinessDaysPerYear),
        // Intersection is bounded above by the most-restrictive constituent: any holiday in
        // any single calendar closes the composite.
        CalendarCompositionMode.All => _calendars.Min(c => c.BusinessDaysPerYear),
        _ => throw new InvalidOperationException($"Unknown composition mode: {_mode}."),
    };

    /// <inheritdoc />
    public override bool IsBusinessDay(DateOnly date) => _mode switch
    {
        CalendarCompositionMode.Any => _calendars.Any(c => c.IsBusinessDay(date)),
        CalendarCompositionMode.All => _calendars.All(c => c.IsBusinessDay(date)),
        _ => throw new InvalidOperationException($"Unknown composition mode: {_mode}."),
    };
}
