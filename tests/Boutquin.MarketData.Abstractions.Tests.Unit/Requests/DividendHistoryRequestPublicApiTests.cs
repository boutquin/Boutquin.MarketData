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

using Boutquin.MarketData.Abstractions.Requests;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Requests;

/// <summary>
/// Asserts the public API tracking file lists every public member of
/// <see cref="DividendHistoryRequest"/>.
/// </summary>
/// <remarks>
/// The public API analyzer's own diagnostics (RS0016 for an untracked member, RS0017 for a stale
/// entry) are suppressed repository-wide, so nothing in a normal build fails when a new public
/// member ships untracked. This test restores that gate for the request type: it reflects over the
/// members that actually exist and requires an entry for each, so the tracking file cannot silently
/// fall behind the surface it is supposed to describe.
/// </remarks>
public sealed class DividendHistoryRequestPublicApiTests
{
    private const string Fqn = "Boutquin.MarketData.Abstractions.Requests.DividendHistoryRequest";

    // Record members the compiler synthesizes. The tracking file does not list them for any
    // sibling request record, so requiring them here would contradict the established convention.
    private static readonly HashSet<string> s_synthesized =
    [
        "Equals",
        "GetHashCode",
        "ToString",
        "PrintMembers",
        "Deconstruct",
        "<Clone>$",
    ];

    private static string[] TrackedApi() => File.ReadAllLines(PublicApiFilePath());

    private static string PublicApiFilePath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "PublicAPI.Unshipped.txt");
        File.Exists(path).Should().BeTrue(
            because: $"the deployed tracking fixture is expected at {path}");
        return path;
    }

    [Fact]
    public void TrackingFile_DeclaresTheRequestType()
        => TrackedApi().Should().Contain(Fqn);

    [Fact]
    public void TrackingFile_DeclaresThePrimaryConstructor()
        => TrackedApi().Should().Contain(
            line => line.StartsWith($"{Fqn}.DividendHistoryRequest(", StringComparison.Ordinal),
            because: "a consumer binding to the primary constructor is binding to tracked API");

    [Fact]
    public void TrackingFile_DeclaresEveryPublicPropertyAccessor()
    {
        var tracked = TrackedApi();

        foreach (var property in typeof(DividendHistoryRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            tracked.Should().Contain(
                line => line.StartsWith($"{Fqn}.{property.Name}.get -> ", StringComparison.Ordinal),
                because: $"{property.Name} is a public property of the request");

            if (property.SetMethod is { IsPublic: true })
            {
                tracked.Should().Contain(
                    line => line.StartsWith($"{Fqn}.{property.Name}.init -> ", StringComparison.Ordinal),
                    because: $"{property.Name} is settable, so the setter is part of the surface");
            }
        }
    }

    [Fact]
    public void TrackingFile_DeclaresEveryPublicMethod()
    {
        var tracked = TrackedApi();

        var methods = typeof(DividendHistoryRequest)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && !s_synthesized.Contains(m.Name))
            .ToList();

        methods.Should().NotBeEmpty(because: "WithRange is declared on the request");

        foreach (var method in methods)
        {
            tracked.Should().Contain(
                line => line.StartsWith($"{Fqn}.{method.Name}(", StringComparison.Ordinal),
                because: $"{method.Name} is a public method of the request");
        }
    }
}
