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
using Boutquin.MarketData.Testing.Builders;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Testing.Tests.Unit;

public sealed class EnvelopeBuilderTests
{
    [Fact]
    public void Build_produces_envelope_with_default_provenance()
    {
        var envelope = new EnvelopeBuilder<string>()
            .WithPayload("data")
            .Build();

        envelope.Payload.Should().Be("data");
        envelope.Provenance.Should().ContainSingle()
            .Which.Provider.Should().Be(new ProviderCode("test-provider"));
    }

    [Fact]
    public void Build_uses_custom_coverage()
    {
        var envelope = new EnvelopeBuilder<string>()
            .WithPayload("data")
            .WithCoverage(100, 95, 5, 0.95m)
            .Build();

        envelope.Coverage.RequestedPoints.Should().Be(100);
        envelope.Coverage.ReturnedPoints.Should().Be(95);
        envelope.Coverage.MissingPoints.Should().Be(5);
        envelope.Coverage.CoverageRatio.Should().Be(0.95m);
    }

    [Fact]
    public void Build_accumulates_issues()
    {
        var envelope = new EnvelopeBuilder<string>()
            .WithPayload("data")
            .WithIssue(IssueCode.StaleData, IssueSeverity.Warning, "Data is old")
            .WithIssue(IssueCode.MissingField, IssueSeverity.Error, "Field X missing")
            .Build();

        envelope.Issues.Should().HaveCount(2);
    }

    [Fact]
    public void Build_uses_custom_provenance_over_default()
    {
        var envelope = new EnvelopeBuilder<string>()
            .WithPayload("data")
            .WithProvenance(new ProviderCode("my-source"), "my-dataset")
            .Build();

        envelope.Provenance.Should().ContainSingle()
            .Which.Provider.Should().Be(new ProviderCode("my-source"));
    }
}
