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

using Boutquin.MarketData.Abstractions.Diagnostics;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Diagnostics;

public sealed class IssueCodeTests
{
    [Theory]
    [InlineData("stale_data", "STALE_DATA")]
    [InlineData("STALE_DATA", "STALE_DATA")]
    [InlineData("Stale_Data", "STALE_DATA")]
    public void Constructor_NormalizesToUpperCase(string input, string expected)
    {
        new IssueCode(input).Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsNullOrWhitespace(string? input)
    {
        Action act = () => _ = new IssueCode(input!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WellKnown_StaticsHaveCanonicalSpelling()
    {
        IssueCode.MissingField.Value.Should().Be("MISSING_FIELD");
        IssueCode.StaleData.Value.Should().Be("STALE_DATA");
        IssueCode.RateLimited.Value.Should().Be("RATE_LIMITED");
        IssueCode.HolidayGap.Value.Should().Be("HOLIDAY_GAP");
        IssueCode.UnexpectedGap.Value.Should().Be("UNEXPECTED_GAP");
        IssueCode.SchemaDrift.Value.Should().Be("SCHEMA_DRIFT");
        IssueCode.SnapshotStale.Value.Should().Be("SNAPSHOT_STALE");
        IssueCode.PartitionFailed.Value.Should().Be("PARTITION_FAILED");
    }

    [Fact]
    public void WellKnown_Statics_AreIdempotentUnderAdHocConstruction()
    {
        new IssueCode("stale_data").Should().Be(IssueCode.StaleData);
        new IssueCode("MISSING_FIELD").Should().Be(IssueCode.MissingField);
    }

    [Fact]
    public void AdHoc_ConstructionAcceptsUnknownCodes()
    {
        var code = new IssueCode("tiingo_quota_exceeded");
        code.Value.Should().Be("TIINGO_QUOTA_EXCEEDED");
    }

    [Fact]
    public void Equality_IsCaseInsensitiveViaNormalization()
    {
        new IssueCode("stale_data").GetHashCode()
            .Should().Be(new IssueCode("STALE_DATA").GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsCanonicalValue()
    {
        IssueCode.StaleData.ToString().Should().Be("STALE_DATA");
    }

    [Fact]
    public void ToString_OnDefault_ReturnsEmpty()
    {
        default(IssueCode).ToString().Should().Be(string.Empty);
    }

    [Fact]
    public void CompareTo_UsesOrdinalOnCanonicalForm()
    {
        IssueCode.HolidayGap.CompareTo(IssueCode.MissingField).Should().BeNegative();
    }
}
