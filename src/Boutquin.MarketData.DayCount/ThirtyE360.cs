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

namespace Boutquin.MarketData.DayCount;

/// <summary>
/// 30E/360 (Eurobond basis) day-count convention — caps both <c>D1</c> and <c>D2</c>
/// at 30 without any end-of-month adjustment. Standard basis for euro-denominated
/// corporate bonds.
/// </summary>
/// <remarks>
/// <para>
/// Formula (ISDA 2006 Definitions, Section 4.16(g), Eurobond Basis):
/// <c>yf = (360·(Y₂ − Y₁) + 30·(M₂ − M₁) + (d₂ − d₁)) / 360</c>
/// where <c>d₁ = min(D₁, 30)</c> and <c>d₂ = min(D₂, 30)</c>.
/// </para>
/// <para>
/// <strong>Difference from 30/360 (Bond Basis).</strong> 30/360 ISDA applies an
/// end-of-month adjustment — if <c>D1 = 31</c>, <c>D2</c> is capped via a different
/// rule that ties months together. 30E/360 has <em>no</em> such adjustment, so
/// Jan 31 → Feb 28 produces <c>(30·1 + (28 − 30))/360 = 28/360</c> under 30E/360 but
/// <c>(30·1 + 0)/360 = 30/360</c> under 30/360 ISDA. The simpler rule is what makes
/// 30E/360 the eurobond standard.
/// </para>
/// <para>
/// Pipeline role: consumers reach this via <see cref="DayCountResolver.Resolve"/>
/// rather than a direct reference, so moving a euro-bond product from 30E/360 to
/// another convention is a single-line configuration change.
/// </para>
/// </remarks>
public sealed class ThirtyE360 : IYearFractionCalculator
{
    /// <summary>
    /// Shared singleton. The convention is stateless so a single instance is safe for
    /// concurrent use across the process.
    /// </summary>
    public static readonly ThirtyE360 Instance = new();

    private ThirtyE360() { }

    /// <inheritdoc />
    public string Code => "30E/360";

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="endDate"/> is earlier than <paramref name="startDate"/>.
    /// Negative year fractions almost always indicate a schedule-generation bug, so the
    /// convention rejects the input rather than silently accruing backwards.
    /// </exception>
    public double YearFraction(DateOnly startDate, DateOnly endDate)
    {
        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endDate),
                $"endDate ({endDate:O}) must be on or after startDate ({startDate:O}).");
        }

        var d1 = Math.Min(startDate.Day, 30);
        var d2 = Math.Min(endDate.Day, 30);

        var years = endDate.Year - startDate.Year;
        var months = endDate.Month - startDate.Month;
        var days = d2 - d1;

        return ((360 * years) + (30 * months) + days) / 360.0;
    }
}
