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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Abstractions.Results;
using Boutquin.MarketData.Orchestration.Fallback;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace Boutquin.MarketData.Orchestration.Tests.Unit.Fallback;

public sealed class Round2FallbackRegressionTests
{
    private static readonly EconomicSeriesRequest s_request = new(
        new EconomicSeriesId("SERIES"), new(new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 2)));

    [Theory]
    [InlineData("capability")]
    [InlineData("fetch")]
    [InlineData("false")]
    public async Task Noncancellation_capability_failures_are_audited_and_fall_back(string failure)
    {
        var primary = new Adapter("primary", 100, failure);
        var backup = new Adapter("backup", 0, "none");
        var orchestrator = new FallbackOrchestrator<EconomicSeriesRequest, ScalarObservation>(
            NullLogger<FallbackOrchestrator<EconomicSeriesRequest, ScalarObservation>>.Instance);
        var execute = async () => await orchestrator.ExecuteAsync(s_request, [primary, backup],
            new CoverageResponseEvaluator<ScalarObservation>()).ConfigureAwait(true);

        var result = await execute.Should().NotThrowAsync("non-cancellation adapter failures continue the chain");
        result.Subject.SelectedProviderKey.Should().Be(backup.ProviderKey);
        result.Subject.Attempts.Should().HaveCount(2);
        result.Subject.Attempts[0].Succeeded.Should().BeFalse();
        result.Subject.Attempts[0].Outcome.Should().Contain(failure == "false" ? "CanHandle=false" : "InvalidOperationException");
        backup.Fetches.Should().Be(1);
        primary.Fetches.Should().Be(failure == "fetch" ? 1 : 0);
    }

    [Fact]
    public async Task Capability_cancellation_escapes_without_trying_backup()
    {
        var primary = new Adapter("primary", 100, "cancel");
        var backup = new Adapter("backup", 0, "none");
        var orchestrator = new FallbackOrchestrator<EconomicSeriesRequest, ScalarObservation>(
            NullLogger<FallbackOrchestrator<EconomicSeriesRequest, ScalarObservation>>.Instance);
        var execute = async () => await orchestrator.ExecuteAsync(s_request, [primary, backup],
            new CoverageResponseEvaluator<ScalarObservation>()).ConfigureAwait(true);

        await execute.Should().ThrowAsync<OperationCanceledException>();
        primary.Fetches.Should().Be(0);
        backup.Fetches.Should().Be(0);
    }

    private sealed class Adapter(string provider, int priority, string failure)
        : IDataSourceAdapter<EconomicSeriesRequest, ScalarObservation>, IPrioritizedAdapter
    {
        public ProviderCode ProviderKey { get; } = new(provider);
        public int Priority => priority;
        public int Fetches { get; private set; }
        public bool CanHandle(EconomicSeriesRequest request) => failure switch
        {
            "capability" => throw new InvalidOperationException("capability failure"),
            "cancel" => throw new OperationCanceledException("capability cancellation"),
            "false" => false,
            _ => true,
        };

        public Task<DataEnvelope<IReadOnlyList<ScalarObservation>>> FetchAsync(
            EconomicSeriesRequest request, CancellationToken cancellationToken = default)
        {
            Fetches++;
            if (failure == "fetch")
            {
                throw new InvalidOperationException("fetch failure");
            }

            return Task.FromResult(new DataEnvelope<IReadOnlyList<ScalarObservation>>(
                [new(request.Range.From, 7m, "decimal")], new(1, 1, 0, 1m), [], []));
        }
    }
}
