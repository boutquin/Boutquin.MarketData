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

using System.Reflection;

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit.TimeSeries;

/// <summary>
/// Shape contract for the dividend dataset-key builder: the type exists in the Cache package's
/// time-series namespace, is a sealed record carrying a provider string, closes
/// <see cref="IDatasetKeyBuilder{TRequest, TDatasetKey}"/> over the
/// (<see cref="DividendHistoryRequest"/>, <see cref="DividendPaymentDatasetKey"/>) pair, and
/// projects a single-symbol partition request to its one ticker.
/// </summary>
/// <remarks>
/// Assertions are reflective rather than strongly typed so a missing type fails at the assertion
/// (with a diagnosable message) instead of at the compiler. Behavioral guarantees — the
/// empty-symbols guard and the deliberate absence of a single-symbol assert — are pinned by
/// <c>DividendPaymentDatasetKeyBuilderTests</c> against the strongly typed surface.
/// </remarks>
public sealed class DividendPaymentDatasetKeyBuilderContractTests
{
    private const string Fqn = "Boutquin.MarketData.Cache.TimeSeries.DividendPaymentDatasetKeyBuilder";

    private static readonly DateRange s_range =
        new(new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31));

    // Anchored on a sibling builder so the probe targets the Cache assembly the kernel ships.
    private static Type? BuilderType()
        => typeof(PriceBarDatasetKeyBuilder).Assembly.GetType(Fqn, throwOnError: false);

    [Fact]
    public void BuilderType_ExistsInTheCacheTimeSeriesNamespace()
        => BuilderType().Should().NotBeNull(
            because: "the time-series row-store path needs a dividend dataset-key builder; "
                + $"expected {Fqn} to exist");

    [Fact]
    public void BuilderType_IsASealedRecord()
    {
        var type = BuilderType();
        type.Should().NotBeNull();

        type!.IsSealed.Should().BeTrue(because: "every sibling dataset-key builder is sealed");
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Should().NotBeNull(because: "the sibling builders are records, so value equality holds");
    }

    [Fact]
    public void BuilderType_ClosesTheDatasetKeyBuilderInterface_OverTheDividendPair()
        => typeof(IDatasetKeyBuilder<DividendHistoryRequest, DividendPaymentDatasetKey>)
            .IsAssignableFrom(BuilderType()!)
            .Should().BeTrue(
                because: "the container resolves the builder through the closed interface, and the "
                    + "time-series pipeline discovers the dataset-key type from that pair");

    [Fact]
    public void BuilderType_TakesTheProviderNameAsItsSoleConstructorArgument()
    {
        var type = BuilderType();
        type.Should().NotBeNull();

        var ctor = type!.GetConstructors()
            .SingleOrDefault(c => c.GetParameters() is [{ ParameterType: var p }] && p == typeof(string));

        ctor.Should().NotBeNull(
            because: "the provider is resolved at the adapter-selection layer and supplied here, "
                + "exactly as the sibling builders take it");
        ctor!.GetParameters()[0].Name.Should().Be("ProviderName");
    }

    [Fact]
    public void Build_ProjectsTheProviderAndTheSinglePartitionTicker()
    {
        var type = BuilderType();
        type.Should().NotBeNull();

        var builder = Activator.CreateInstance(type!, "iex");
        var request = new DividendHistoryRequest([new Symbol("AAPL")], s_range);

        var key = type!.GetMethod("Build")!.Invoke(builder, [request]);

        key.Should().Be(new DividendPaymentDatasetKey("iex", "AAPL"));
    }
}
