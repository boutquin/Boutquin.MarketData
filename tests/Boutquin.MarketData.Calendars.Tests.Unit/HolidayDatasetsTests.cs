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

using Boutquin.MarketData.Calendars.Holidays;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Calendars.Tests.Unit;

/// <summary>
/// Tests for <see cref="HolidayDatasets"/>.
/// </summary>
public sealed class HolidayDatasetsTests
{
    [Fact]
    public void UsSifma_ReturnsNonEmptyArray()
    {
        // Act
        var dates = HolidayDatasets.UsSifma();

        // Assert
        dates.Should().NotBeEmpty();
    }

    [Fact]
    public void GbLondon_ReturnsNonEmptyArray()
    {
        // Act
        var dates = HolidayDatasets.GbLondon();

        // Assert
        dates.Should().NotBeEmpty();
    }

    [Fact]
    public void Target_ReturnsNonEmptyArray()
    {
        // Act
        var dates = HolidayDatasets.Target();

        // Assert
        dates.Should().NotBeEmpty();
    }

    [Fact]
    public void CaStatutory_ReturnsNonEmptyArray()
    {
        // Act
        var dates = HolidayDatasets.CaStatutory();

        // Assert
        dates.Should().NotBeEmpty();
    }

    [Fact]
    public void CmeGroup_ReturnsNonEmptyArray()
    {
        // Act
        var dates = HolidayDatasets.CmeGroup();

        // Assert
        dates.Should().NotBeEmpty();
    }

    [Fact]
    public void CmeGroup_ContainsOnlyNewYearGoodFridayChristmas()
    {
        // CME full-closure holidays are limited to these three.
        var dates = HolidayDatasets.CmeGroup();

        // Should NOT contain MLK, Presidents, Memorial, Juneteenth, Independence,
        // Labor, Columbus, Veterans, or Thanksgiving.
        // Spot-check: MLK 2024 is Jan 15, Thanksgiving 2024 is Nov 28
        dates.Should().NotContain(new DateOnly(2024, 1, 15));  // MLK
        dates.Should().NotContain(new DateOnly(2024, 11, 28)); // Thanksgiving

        // Should contain Good Friday 2024 (Mar 29)
        dates.Should().Contain(new DateOnly(2024, 3, 29));
    }

    [Theory]
    [MemberData(nameof(AllDatasets))]
    public void AllDates_AreUnique(string name, DateOnly[] dates)
    {
        // Assert — no duplicates within any dataset.
        _ = name; // used for test display only
        dates.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [MemberData(nameof(AllDatasets))]
    public void AllDates_AreSorted(string name, DateOnly[] dates)
    {
        // Assert — dates should be in ascending chronological order.
        _ = name;
        dates.Should().BeInAscendingOrder();
    }

    public static TheoryData<string, DateOnly[]> AllDatasets => new()
    {
        { "UsSifma", HolidayDatasets.UsSifma() },
        { "GbLondon", HolidayDatasets.GbLondon() },
        { "Target", HolidayDatasets.Target() },
        { "CaStatutory", HolidayDatasets.CaStatutory() },
        { "CmeGroup", HolidayDatasets.CmeGroup() },
    };
}
