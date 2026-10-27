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
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Results;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Results;

public sealed class DataEnvelopeTests
{
    private static readonly DataCoverage s_coverage = new(10, 10, 0, 1.0m);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Metadata_InputMutation_DoesNotChangeAudit(bool useInit, bool mutateIssues)
    {
        var issue = new DataIssue(IssueCode.StaleData, IssueSeverity.Error, "Unreliable data");
        var source = new DataProvenance(new ProviderCode("tiingo"), "AAPL", LicenseType.Free,
            RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UnixEpoch, null);
        var issues = new List<DataIssue> { issue };
        var provenance = new List<DataProvenance> { source };
        var original = new DataEnvelope<string>("payload", s_coverage, [], []);
        var envelope = useInit
            ? original with { Issues = issues, Provenance = provenance }
            : new DataEnvelope<string>("payload", s_coverage, issues, provenance);

        if (mutateIssues)
        {
            issues.Clear();
        }
        else
        {
            provenance.Clear();
        }

        envelope.Issues.Should().Equal(issue);
        envelope.Provenance.Should().Equal(source);
        envelope.Payload.Should().Be("payload");
        envelope.Coverage.Should().BeSameAs(s_coverage);
        original.Issues.Should().BeEmpty();
        original.Provenance.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Metadata_OutboundMutation_DoesNotChangeAudit(bool mutateIssues)
    {
        var issue = new DataIssue(IssueCode.StaleData, IssueSeverity.Error, "Unreliable data");
        var source = new DataProvenance(new ProviderCode("tiingo"), "AAPL", LicenseType.Free,
            RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UnixEpoch, null);
        DataIssue[] inputIssues = [issue];
        DataProvenance[] inputProvenance = [source];
        var envelope = new DataEnvelope<string>("payload", s_coverage, inputIssues, inputProvenance);

        if (mutateIssues && envelope.Issues is IList<DataIssue> issues)
        {
            try
            {
                issues[0] = issue with { Severity = IssueSeverity.Warning };
            }
            catch (NotSupportedException)
            {
                // Read-only wrappers may refuse mutation; detached copies may accept it.
            }
        }
        else if (!mutateIssues && envelope.Provenance is IList<DataProvenance> provenance)
        {
            try
            {
                provenance[0] = source with { Provider = new ProviderCode("other") };
            }
            catch (NotSupportedException)
            {
                // Either exposure strategy must preserve the envelope's audit metadata.
            }
        }

        envelope.Issues.Should().Equal(issue);
        envelope.Provenance.Should().Equal(source);
    }

    [Fact]
    public void Construction_PreservesAllFourFields()
    {
        // Arrange
        var payload = "test-payload";
        var issues = new List<DataIssue> { new(IssueCode.StaleData, IssueSeverity.Warning, "Data is 2 days old") };
        var provenance = new List<DataProvenance>
        {
            new(new ProviderCode("tiingo"), "AAPL", LicenseType.Free, RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UtcNow, "https://api.tiingo.com")
        };

        // Act
        var envelope = new DataEnvelope<string>(payload, s_coverage, issues, provenance);

        // Assert
        envelope.Payload.Should().Be(payload);
        envelope.Coverage.Should().Be(s_coverage);
        envelope.Issues.Should().HaveCount(1);
        envelope.Provenance.Should().HaveCount(1);
    }

    [Fact]
    public void EmptyIssuesList_IsValid()
    {
        // Arrange & Act
        var envelope = new DataEnvelope<int>(
            42,
            s_coverage,
            Array.Empty<DataIssue>(),
            new List<DataProvenance>
            {
                new(new ProviderCode("fred"), "GDPC1", LicenseType.Free, RetrievalMode.Api, FreshnessClass.Live, DateTimeOffset.UtcNow, null)
            });

        // Assert
        envelope.Issues.Should().BeEmpty();
    }

    [Fact]
    public void EmptyProvenanceList_IsValid()
    {
        // Arrange & Act
        var envelope = new DataEnvelope<int>(
            42,
            s_coverage,
            new List<DataIssue> { new(new IssueCode("TEST"), IssueSeverity.Warning, "test") },
            Array.Empty<DataProvenance>());

        // Assert
        envelope.Provenance.Should().BeEmpty();
    }
}
