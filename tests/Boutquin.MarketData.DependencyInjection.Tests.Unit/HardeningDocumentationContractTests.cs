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

using System.Text.RegularExpressions;
using System.Xml.Linq;

using Boutquin.MarketData.Cache.Memory;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

public sealed class HardeningDocumentationContractTests
{
    [Fact]
    public void SnapshotDocumentation_StatesPerTypeFirstReadAndSupportedRecordBoundary()
    {
        var abstraction = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Boutquin.MarketData.Abstractions.xml"));
        var orchestration = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Boutquin.MarketData.Orchestration.xml"));
        string[] members =
        [
            "M:Boutquin.MarketData.Abstractions.Contracts.IDataPipeline.AcquireReadSnapshotAsync(System.Threading.CancellationToken)",
            "T:Boutquin.MarketData.Abstractions.Snapshots.IDataSnapshot",
            "T:Boutquin.MarketData.Orchestration.Snapshots.DataSnapshot"
        ];
        foreach (var name in members)
        {
            var member = abstraction.Descendants("member").Concat(orchestration.Descendants("member"))
                .Single(element => (string?)element.Attribute("name") == name);
            var text = Regex.Replace(member.Value, @"\s+", " ");
            SnapshotClaimIsAccurate(text).Should().BeTrue(because: $"{name} must describe the actual capture and type boundaries");
            SnapshotClaimIsAccurate(text + " Every record type is available at handle acquisition.").Should().BeFalse();
        }

        SnapshotClaimIsAccurate(Read("docs/time-series-cache.md")).Should().BeTrue();
    }

    private static bool SnapshotClaimIsAccurate(string text) =>
        text.Contains("first read for each record type", StringComparison.Ordinal)
        && text.Contains("not a cross-record-type", StringComparison.Ordinal)
        && text.Contains("columnar-rehydratable", StringComparison.Ordinal)
        && text.Contains("DividendPayment", StringComparison.Ordinal)
        && text.Contains("FuturesSettlement", StringComparison.Ordinal)
        && !text.Contains("Every record type is available at handle acquisition", StringComparison.Ordinal);

    [Fact]
    public async Task CacheDocumentation_DescribesTheObservedOverwriteContract()
    {
        var cache = new ExactOnceMemoryCache();
        await cache.SetAsync("same-key", "first", TimeSpan.FromHours(1));
        await cache.SetAsync("same-key", "second", TimeSpan.FromHours(1));
        (await cache.GetAsync<string>("same-key")).Should().Be("second");

        var readme = Read("README.md");
        CacheClaimIsAccurate(readme).Should().BeTrue();
        CacheClaimIsAccurate(readme + "\nL1 provides exact-once writes.").Should().BeFalse();
        CacheClaimIsAccurate(readme + "\nL1 provides exactly-once writes.").Should().BeFalse();
    }

    [Fact]
    public void ReferenceGuide_DistinguishesExceptionsFromRecoverableEnvelopes()
    {
        var guide = Read("docs/reference-data.md");
        ErrorClaimIsAccurate(guide).Should().BeTrue();
        ErrorClaimIsAccurate(guide + "\nEvery MarketData failure surfaces as a MarketDataException.").Should().BeFalse();
    }

    [Fact]
    public void ReferenceGuide_IdentifiesPublicSurfacesWithoutPrivatePlanningLinks()
    {
        var guide = Read("docs/reference-data.md");
        PublicGuideIsConsumerFacing(guide).Should().BeTrue();
        PublicGuideIsConsumerFacing(guide + "\n**Status:** awaiting acceptance criteria.").Should().BeFalse();
        PublicGuideIsConsumerFacing(guide + "\n[Private plan](../specs/feature-reference.md)").Should().BeFalse();
        PublicGuideIsConsumerFacing(guide.Replace("StandardBenchmarks", "InternalRegistry", StringComparison.Ordinal))
            .Should().BeFalse();
        PublicGuideIsConsumerFacing(guide.Replace("Boutquin.Domain.Enumerations", "Private.Currency", StringComparison.Ordinal))
            .Should().BeFalse();
    }

    [Fact]
    public void StalenessXml_DescribesUtcClockAndInclusiveTail()
    {
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Boutquin.MarketData.Orchestration.xml"));
        var member = xml.Descendants("member").Single(element =>
            (string?)element.Attribute("name") == "P:Boutquin.MarketData.Orchestration.Pipeline.DataPipelineOptions.TimeSeriesStalenessMaxAge");
        var text = Regex.Replace(member.Value, @"\s+", " ");
        StalenessClaimIsAccurate(text).Should().BeTrue();
        StalenessClaimIsAccurate(text + " newest + 1").Should().BeFalse();
        StalenessClaimIsAccurate(text.Replace("UTC clock", "request end", StringComparison.Ordinal)).Should().BeFalse();
    }

    [Fact]
    public void TransportXml_DoesNotClaimDnsResolutionOrRebindingProtection()
    {
        var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Boutquin.MarketData.Transport.xml"));
        var member = xml.Descendants("member").Single(element =>
            (string?)element.Attribute("name") == "T:Boutquin.MarketData.Transport.Http.ResilientHttpDataTransport");
        var text = Regex.Replace(member.Value, @"\s+", " ");
        TransportClaimIsAccurate(text).Should().BeTrue();
        TransportClaimIsAccurate(text + " Rejects hosts that resolve to a blocked range.").Should().BeFalse();
    }

    private static bool CacheClaimIsAccurate(string text) =>
        text.Contains("last-write-wins", StringComparison.Ordinal)
        && !Regex.IsMatch(text, @"exact(?:ly)?-once", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ErrorClaimIsAccurate(string text) =>
        text.Contains("ArgumentException", StringComparison.Ordinal)
        && text.Contains("MarketDataException", StringComparison.Ordinal)
        && text.Contains("FETCH_FAILED", StringComparison.Ordinal)
        && text.Contains("Missing", StringComparison.Ordinal)
        && text.Contains("DataIssue", StringComparison.Ordinal)
        && !Regex.IsMatch(text, @"every\s+MarketData\s+failure\s+(?:surfaces|throws)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool PublicGuideIsConsumerFacing(string text) =>
        !text.Contains("acceptance criteria", StringComparison.OrdinalIgnoreCase)
        && !text.Contains("private development repository", StringComparison.OrdinalIgnoreCase)
        && !text.Contains("**Status:**", StringComparison.OrdinalIgnoreCase)
        && !Regex.IsMatch(text, @"\]\([^)]*\bspecs/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        && text.Contains("Boutquin.MarketData.Abstractions.ReferenceData", StringComparison.Ordinal)
        && text.Contains("Boutquin.Domain.Enumerations", StringComparison.Ordinal)
        && text.Contains("StandardBenchmarks", StringComparison.Ordinal)
        && text.Contains("InstrumentConventionRegistry", StringComparison.Ordinal)
        && text.Contains("IFixingsStore", StringComparison.Ordinal);

    private static bool StalenessClaimIsAccurate(string text) =>
        text.Contains("UTC clock", StringComparison.Ordinal)
        && text.Contains("inclusive", StringComparison.Ordinal)
        && text.Contains("request.To", StringComparison.Ordinal)
        && !text.Contains("newest + 1", StringComparison.Ordinal);

    private static bool TransportClaimIsAccurate(string text) =>
        text.Contains("literal-IP", StringComparison.Ordinal)
        && text.Contains("does not resolve DNS", StringComparison.Ordinal)
        && text.Contains("does not prevent DNS rebinding", StringComparison.Ordinal)
        && !Regex.IsMatch(text, @"rejects\s+hosts\s+that\s+resolve", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Read(string relativePath)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Boutquin.MarketData.slnx")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(root!.FullName, relativePath));
    }
}
