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

using Boutquin.MarketData.Abstractions.Observability;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// AC-5 — <see cref="DataQualityIncident"/> record shape; an in-memory
/// <see cref="IDataQualityIncidentSink"/> captures incidents; the null-sink emit path falls
/// back to <c>ILogger</c> without throwing.
/// </summary>
public sealed class DataQualityIncidentSinkTests
{
    /// <summary>Test-only in-memory sink fixture capturing every reported incident.</summary>
    private sealed class CapturingSink : IDataQualityIncidentSink
    {
        public List<DataQualityIncident> Captured { get; } = [];

        public void Report(DataQualityIncident incident) => Captured.Add(incident);
    }

    private static DataQualityIncident SampleIncident() => new(
        DataQualityIncidentKind.RowGroupQuarantine,
        RecordType: "Bar",
        DatasetKey: "tiingo/Daily/AGG",
        OccurredAtUtc: new DateTimeOffset(2026, 6, 13, 9, 16, 0, TimeSpan.Zero),
        Detail: "row group 3 failed CRC32C");

    [Fact]
    public void DataQualityIncident_CarriesAllFields()
    {
        var incident = SampleIncident();

        incident.Kind.Should().Be(DataQualityIncidentKind.RowGroupQuarantine);
        incident.RecordType.Should().Be("Bar");
        incident.DatasetKey.Should().Be("tiingo/Daily/AGG");
        incident.OccurredAtUtc.Should().Be(new DateTimeOffset(2026, 6, 13, 9, 16, 0, TimeSpan.Zero));
        incident.Detail.Should().Be("row group 3 failed CRC32C");
    }

    [Fact]
    public void ReportOrLog_WithSink_RoutesToSink()
    {
        var sink = new CapturingSink();
        var incident = SampleIncident();

        sink.ReportOrLog(incident, NullLogger.Instance);

        sink.Captured.Should().ContainSingle().Which.Should().Be(incident);
    }

    [Fact]
    public void ReportOrLog_WithNullSink_FallsBackToLoggerWithoutThrowing()
    {
        IDataQualityIncidentSink? sink = null;

        var act = () => sink.ReportOrLog(SampleIncident(), NullLogger.Instance);

        act.Should().NotThrow();
    }
}
