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
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.Storage.Domain.ValueObjects;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit.Snapshots;

public sealed class SnapshotEnvelopeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Metadata_owns_audit_collections(bool useInit, bool useReadOnlyView)
    {
        var original = new DataProvenance(new ProviderCode("original"), "AAPL", LicenseType.Commercial,
            RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UnixEpoch, null);
        var issue = new DataIssue(IssueCode.MissingField, IssueSeverity.Error, "Missing audited field");
        var provenance = new List<DataProvenance> { original };
        var issues = new List<DataIssue> { issue };
        IReadOnlyList<DataProvenance> provenanceInput = useReadOnlyView ? provenance.AsReadOnly() : provenance;
        IReadOnlyList<DataIssue> issuesInput = useReadOnlyView ? issues.AsReadOnly() : issues;
        var metadata = useInit
            ? new SnapshotMetadata(DateTimeOffset.UnixEpoch, [], []) with { Provenance = provenanceInput, Issues = issuesInput }
            : new SnapshotMetadata(DateTimeOffset.UnixEpoch, provenanceInput, issuesInput);

        provenance.Clear();
        issues.Clear();

        metadata.Provenance.Should().ContainSingle().Which.Should().Be(original);
        metadata.Issues.Should().ContainSingle().Which.Should().Be(issue);
    }

    [Fact]
    public void Metadata_getters_refuse_audit_mutation()
    {
        var original = new DataProvenance(new ProviderCode("original"), "AAPL", LicenseType.Commercial,
            RetrievalMode.Api, FreshnessClass.EndOfDay, DateTimeOffset.UnixEpoch, null);
        var issue = new DataIssue(IssueCode.MissingField, IssueSeverity.Error, "Missing audited field");
        var metadata = new SnapshotMetadata(DateTimeOffset.UnixEpoch, new[] { original }, new[] { issue });

        Action mutateProvenance = () => ((IList<DataProvenance>)metadata.Provenance)[0] = original with { LicenseFlag = LicenseType.Free };
        Action mutateIssues = () => ((IList<DataIssue>)metadata.Issues)[0] = issue with { Message = "Changed" };

        mutateProvenance.Should().Throw<NotSupportedException>();
        mutateIssues.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Construction_preserves_all_fields()
    {
        // Arrange
        var storedAt = new DateTimeOffset(2024, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var provenance = new List<DataProvenance>
        {
            new(new ProviderCode("tiingo"), "AAPL", LicenseType.Free, RetrievalMode.Api, FreshnessClass.EndOfDay, storedAt, null)
        };
        var metadata = new SnapshotMetadata(storedAt, provenance, []);
        var payload = new List<string> { "record1", "record2" };
        var version = new SchemaVersion(1);

        // Act
        var envelope = new SnapshotEnvelope<IReadOnlyList<string>>(version, metadata, payload);

        // Assert
        envelope.Version.Should().Be(version);
        envelope.Metadata.StoredAtUtc.Should().Be(storedAt);
        envelope.Metadata.Provenance.Should().HaveCount(1);
        envelope.Payload.Should().HaveCount(2);
    }

    [Fact]
    public void Record_equality_works_for_same_values()
    {
        // Arrange
        var storedAt = DateTimeOffset.UtcNow;
        var provenance = Array.Empty<DataProvenance>();
        var meta1 = new SnapshotMetadata(storedAt, provenance, []);
        var meta2 = new SnapshotMetadata(storedAt, provenance, []);

        // Act & Assert
        meta1.Should().Be(meta2);
    }

    [Fact]
    public void Empty_provenance_is_valid()
    {
        // Arrange & Act
        var metadata = new SnapshotMetadata(DateTimeOffset.UtcNow, Array.Empty<DataProvenance>(), []);
        var envelope = new SnapshotEnvelope<string>(new SchemaVersion(1), metadata, "payload");

        // Assert
        envelope.Metadata.Provenance.Should().BeEmpty();
        envelope.Payload.Should().Be("payload");
    }
}
