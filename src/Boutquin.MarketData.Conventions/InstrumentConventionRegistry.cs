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

namespace Boutquin.MarketData.Conventions;

/// <summary>
/// Stores instrument conventions keyed by <see cref="IInstrumentConvention.Code"/> and
/// resolves them by code at lookup time.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Idempotency.</strong> <see cref="Add"/> is last-write-wins: registering a
/// convention whose <c>Code</c> already exists in the registry silently overwrites
/// the previous entry. Callers that need "add once" semantics must check the
/// registry first. <see cref="CreateDefault"/> returns a fresh registry instance on
/// every call, so default registries are independent.
/// </para>
/// <para>
/// <strong>Lookup semantics.</strong> Keys compare case-insensitively (ordinal) so
/// <c>"USD-SOFR-OIS"</c> and <c>"usd-sofr-ois"</c> resolve to the same entry.
/// </para>
/// </remarks>
public sealed class InstrumentConventionRegistry
{
    private readonly Dictionary<string, IInstrumentConvention> _conventions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Adds or replaces an instrument convention in the registry.
    /// </summary>
    /// <param name="convention">Convention definition keyed by its <see cref="IInstrumentConvention.Code"/>.</param>
    /// <returns>The current registry instance for fluent chaining.</returns>
    public InstrumentConventionRegistry Add(IInstrumentConvention convention)
    {
        ArgumentNullException.ThrowIfNull(convention);
        _conventions[convention.Code] = convention;
        return this;
    }

    /// <summary>
    /// Resolves a convention by code.
    /// </summary>
    /// <param name="code">Convention code to resolve.</param>
    /// <returns>The convention mapped to <paramref name="code"/>.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no convention has been registered under <paramref name="code"/>.</exception>
    public IInstrumentConvention GetRequired(string code)
    {
        if (!_conventions.TryGetValue(code, out var convention))
        {
            throw new KeyNotFoundException($"Convention '{code}' was not found.");
        }

        return convention;
    }

    /// <summary>
    /// Creates a registry pre-populated with baseline market conventions — the same
    /// four-currency OIS/FRA/futures/swap-leg entries historically emitted by
    /// <c>Boutquin.Curves.Conventions.InstrumentConventionRegistry.CreateDefault</c>.
    /// </summary>
    /// <remarks>Defaults are editable application baselines. They do not select a trading venue,
    /// clearing agreement, contractual observation window, or administrator publication calendar.
    /// Register the terms of the actual instrument before using them to construct cash flows.</remarks>
    /// <returns>A registry containing built-in OIS, FRA, futures, and swap-leg conventions.</returns>
    public static InstrumentConventionRegistry CreateDefault() =>
        new InstrumentConventionRegistry()
            .Add(new OisConvention("USD-SOFR-OIS", "USNY", "ACT/360", PaymentFrequency.Annual, BusinessDayAdjustment.ModifiedFollowing, 2, 2, ObservationShiftConvention.Shifted))
            .Add(new OisConvention("GBP-SONIA-OIS", "GBLO", "ACT/365F", PaymentFrequency.Annual, BusinessDayAdjustment.ModifiedFollowing, 0, 0, ObservationShiftConvention.Shifted))
            .Add(new OisConvention("EUR-ESTR-OIS", "TARGET", "ACT/360", PaymentFrequency.Annual, BusinessDayAdjustment.ModifiedFollowing, 2, 2, ObservationShiftConvention.Shifted))
            .Add(new OisConvention("CAD-CORRA-OIS", "CATO", "ACT/365F", PaymentFrequency.Annual, BusinessDayAdjustment.ModifiedFollowing, 1, 1, ObservationShiftConvention.Shifted))
            .Add(new FraConvention("USD-FRA", "USNY", "ACT/360", BusinessDayAdjustment.ModifiedFollowing, 2))
            .Add(new FutureContractConvention("USD-SR3", "USNY", "ACT/360", "IMM"))
            .Add(new SwapLegConvention("USD-FIXED-6M-30-360", "30/360", PaymentFrequency.SemiAnnual, BusinessDayAdjustment.ModifiedFollowing, RollConvention.DayOfMonth, 2, false))
            .Add(new SwapLegConvention("GBP-FIXED-1Y-ACT-365F", "ACT/365F", PaymentFrequency.Annual, BusinessDayAdjustment.ModifiedFollowing, RollConvention.DayOfMonth, 0, false, "GBLO"))
            .Add(new SwapLegConvention("EUR-FIXED-1Y-ACT-360", "ACT/360", PaymentFrequency.Annual, BusinessDayAdjustment.ModifiedFollowing, RollConvention.DayOfMonth, 2, false, "TARGET"));
}
