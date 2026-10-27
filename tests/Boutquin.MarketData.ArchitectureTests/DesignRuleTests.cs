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

public sealed class DesignRuleTests : BaseArchitectureTest
{
    [Fact]
    public void AllClasses_ShouldBeSealed()
    {
        foreach (var assembly in AllSourceAssemblies)
        {
            var result = Types
                .InAssembly(assembly)
                .That()
                .AreClasses()
                .And()
                .AreNotStatic()
                .And()
                .AreNotAbstract()
                .And()
                // Coverlet's coverage collector injects a non-sealed tracker class named
                // "Coverlet.Core.Instrumentation.Tracker.<AssemblyName>_<guid>" into every
                // instrumented assembly in CI. The tracker is private to Coverlet and outside
                // our sealing policy — exclude it so the rule stays enforceable without
                // weakening coverage instrumentation. Matches the CLAUDE.md guidance.
                .DoNotResideInNamespace("Coverlet.Core.Instrumentation")
                .Should()
                .BeSealed()
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                because: $"all non-static non-abstract classes must be sealed [{assembly.GetName().Name}: {GetFailingTypes(result)}]");
        }
    }

    [Fact]
    public void StronglyTypedIds_ShouldBeReadonlyRecordStructs()
    {
        var catalogIdTypes = CatalogAssembly
            .GetTypes()
            .Where(t => t.Namespace == "Boutquin.MarketData.Catalog.Identifiers"
                && t.IsPublic).ToArray();

        catalogIdTypes.Should().NotBeEmpty();

        foreach (var type in catalogIdTypes)
        {
            type.IsValueType.Should().BeTrue(
                because: $"{type.Name} should be a value type (struct)");

            type.IsDefined(typeof(System.Runtime.CompilerServices.IsReadOnlyAttribute), inherit: false)
                .Should().BeTrue(because: $"{type.Name} should be a readonly struct");

            var isRecord = type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) != null;
            isRecord.Should().BeTrue(
                because: $"{type.Name} should be a record struct");
        }
    }
}
