// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

namespace Boutquin.MarketData.ArchitectureTests;

public sealed class DependencyTests : BaseArchitectureTest
{
    [Fact]
    public void Abstractions_ShouldNotDependOnAnyOtherMarketDataAssembly()
    {
        var result = Types
            .InAssembly(AbstractionsAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Boutquin.MarketData.Transport",
                "Boutquin.MarketData.Cache",
                "Boutquin.MarketData.Storage",
                "Boutquin.MarketData.Catalog",
                "Boutquin.MarketData.DayCount",
                "Boutquin.MarketData.Calendars",
                "Boutquin.MarketData.Conventions",
                "Boutquin.MarketData.Normalization",
                "Boutquin.MarketData.Orchestration",
                "Boutquin.MarketData.DependencyInjection",
                "Boutquin.MarketData.Testing")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Abstractions is the foundation and must not depend on any other MarketData assembly [{GetFailingTypes(result)}]");
    }

    [Theory]
    [InlineData(nameof(TransportAssembly))]
    [InlineData(nameof(CacheAssembly))]
    [InlineData(nameof(StorageAssembly))]
    [InlineData(nameof(CatalogAssembly))]
    [InlineData(nameof(DayCountAssembly))]
    [InlineData(nameof(CalendarsAssembly))]
    [InlineData(nameof(ConventionsAssembly))]
    public void InfrastructurePackages_ShouldNotDependOnCompositionOrDiLayers(string assemblyName)
    {
        var assembly = GetAssemblyByName(assemblyName);

        var result = Types
            .InAssembly(assembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Boutquin.MarketData.Normalization",
                "Boutquin.MarketData.Orchestration",
                "Boutquin.MarketData.DependencyInjection")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"infrastructure packages must not depend on composition or DI layers [{assembly.GetName().Name}: {GetFailingTypes(result)}]");
    }

    [Fact]
    public void Normalization_ShouldNotDependOnOrchestrationOrDi()
    {
        var result = Types
            .InAssembly(NormalizationAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Boutquin.MarketData.Orchestration",
                "Boutquin.MarketData.DependencyInjection")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Normalization must not depend on Orchestration or DI [{GetFailingTypes(result)}]");
    }

    [Fact]
    public void Testing_ShouldOnlyDependOnAbstractions()
    {
        var result = Types
            .InAssembly(TestingAssembly)
            .Should()
            .NotHaveDependencyOnAny(
                "Boutquin.MarketData.Transport",
                "Boutquin.MarketData.Cache",
                "Boutquin.MarketData.Storage",
                "Boutquin.MarketData.Catalog",
                "Boutquin.MarketData.DayCount",
                "Boutquin.MarketData.Calendars",
                "Boutquin.MarketData.Normalization",
                "Boutquin.MarketData.Orchestration",
                "Boutquin.MarketData.DependencyInjection")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"Testing must only depend on Abstractions [{GetFailingTypes(result)}]");
    }

    /// <summary>
    /// Enforces the three-layer data architecture — MarketData is the shared ingestion
    /// kernel and must never reference a domain repo. <c>Boutquin.Analytics</c> stays in
    /// the forbid-list as a defensive alias after its 2026 rename to
    /// <c>Boutquin.Curves</c>, so an accidental resurrection of the old package name
    /// fails this test rather than silently reintroducing a cross-layer cycle.
    /// </summary>
    [Fact]
    public void AllAssemblies_ShouldNotDependOnDomainRepos()
    {
        foreach (var assembly in AllSourceAssemblies)
        {
            var result = Types
                .InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(
                    "Boutquin.Curves",
                    "Boutquin.Analytics",
                    "Boutquin.Trading",
                    "Boutquin.OptionPricing")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"MarketData must not depend on Curves, Trading, or OptionPricing [{assembly.GetName().Name}: {GetFailingTypes(result)}]");
        }
    }

    private static Assembly GetAssemblyByName(string name) => name switch
    {
        nameof(TransportAssembly) => TransportAssembly,
        nameof(CacheAssembly) => CacheAssembly,
        nameof(StorageAssembly) => StorageAssembly,
        nameof(CatalogAssembly) => CatalogAssembly,
        nameof(DayCountAssembly) => DayCountAssembly,
        nameof(CalendarsAssembly) => CalendarsAssembly,
        nameof(ConventionsAssembly) => ConventionsAssembly,
        _ => throw new ArgumentException($"Unknown assembly: {name}"),
    };
}
