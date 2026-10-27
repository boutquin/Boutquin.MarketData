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
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Boutquin.MarketData.Calendars.Holidays;
using Boutquin.MarketData.DayCount;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Tests.Integration;

public sealed class FinancialOracleCorpusTests
{
    private static JsonDocument ReadCorpus() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "financial-oracle.json")));
    private static DateOnly Date(JsonElement row, string field) => DateOnly.Parse(
        row.GetProperty(field).GetString()!, CultureInfo.InvariantCulture);

    [Fact]
    public void Gregorian_Easter_matches_independent_dateutil_across_its_documented_range()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "easter-oracle.json")));
        var method = typeof(UsNyseCalendar).Assembly
            .GetType("Boutquin.MarketData.Calendars.Holidays.CalendarHelpers", throwOnError: true)!
            .GetMethod("ComputeEaster", BindingFlags.NonPublic | BindingFlags.Static)!;
        var dates = corpus.RootElement.GetProperty("dates").EnumerateArray().ToArray();
        dates.Should().HaveCount(2517);
        foreach (var item in dates)
        {
            var expected = DateOnly.Parse(item.GetString()!, CultureInfo.InvariantCulture);
            var actual = (DateOnly)method.Invoke(null, [expected.Year])!;
            actual.Should().Be(expected);
        }
    }

    [Theory]
    [InlineData("USNY")]
    [InlineData("CATO")]
    [InlineData("GBLO")]
    [InlineData("TARGET")]
    [InlineData("USSF")]
    public void Modern_calendar_matches_independent_QuantLib_corpus(string code)
    {
        using var corpus = ReadCorpus();
        var root = corpus.RootElement;
        var start = Date(root, "calendarStart");
        var expected = root.GetProperty("calendars").GetProperty(code).GetString()!;
        var calendar = HolidayCalendarFactory.Create(code);
        var differences = new List<string>();
        for (var i = 0; i < expected.Length; i++)
        {
            var date = start.AddDays(i);
            if (calendar.IsBusinessDay(date) != (expected[i] == '1'))
            {
                differences.Add($"{code} {date:yyyy-MM-dd}: expected business={expected[i]}");
            }
        }
        differences.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ACT/360")]
    [InlineData("ACT/365F")]
    [InlineData("30/360")]
    [InlineData("30E/360")]
    public void Day_count_matches_independent_QuantLib_corpus(string code)
    {
        using var corpus = ReadCorpus();
        var calculator = DayCountResolver.Resolve(code);
        var count = 0;
        foreach (var row in corpus.RootElement.GetProperty("dayCounts").EnumerateArray())
        {
            if (row.GetProperty("code").GetString() != code)
            {
                continue;
            }
            var start = Date(row, "start");
            var end = Date(row, "end");
            calculator.YearFraction(start, end).Should().BeApproximately(row.GetProperty("expected").GetDouble(),
                1e-12, $"{code}: {start} to {end}");
            count++;
        }
        count.Should().Be(1160);
    }

    [Fact]
    public void Coupon_context_ICMA_matches_independent_QuantLib_corpus()
    {
        using var corpus = ReadCorpus();
        foreach (var row in corpus.RootElement.GetProperty("icma").EnumerateArray())
        {
            var actual = ActualActualInPeriod.Instance.YearFraction(Date(row, "start"), Date(row, "end"),
                Date(row, "periodStart"), Date(row, "periodEnd"), row.GetProperty("frequency").GetInt32());
            actual.Should().BeApproximately(row.GetProperty("expected").GetDouble(), 1e-12);
        }
        corpus.RootElement.GetProperty("icma").GetArrayLength().Should().Be(60);
    }
}
