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

using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit;

/// <summary>
/// Pins the dividend-classification doc contract against the generated documentation file that
/// ships inside the package, rather than against source text.
/// </summary>
/// <remarks>
/// The classification policy on <c>DividendType</c> is the contract adapter authors implement
/// against — it is read in IntelliSense, not in this repository — so the assertions below are made
/// on the emitted XML that a consumer actually receives. These are contract assertions in the same
/// sense as a signature assertion: an adapter that emits an unclassified aggregate is either
/// sanctioned or in violation depending on which words ship.
/// </remarks>
public sealed class DividendDocContractTests
{
    private const string DividendPaymentDoc =
        "T:Boutquin.MarketData.Abstractions.Records.DividendPayment";

    private const string DividendTypeDoc =
        "T:Boutquin.MarketData.Abstractions.ReferenceData.DividendType";

    private const string DividendHistoryRequestDoc =
        "T:Boutquin.MarketData.Abstractions.Requests.DividendHistoryRequest";

    private const string DividendHistoryDatasetKeyDoc =
        "P:Boutquin.MarketData.Abstractions.Requests.DividendHistoryRequest.DatasetKey";

    private static readonly string s_generatedDocs = LoadGeneratedDocs();

    private const string ContradictoryDividendTypePolicy = """
        Provider-aggregated amounts must never use Unclassified. The inference is not logged.
        A single payment must not use the jurisdictional default.
        """;

    private static string LoadGeneratedDocs()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Boutquin.MarketData.Abstractions.xml");
        File.Exists(path).Should().BeTrue(
            because: $"the Abstractions package emits its documentation file next to the assembly; looked in {path}");
        return File.ReadAllText(path);
    }

    private static string MemberDoc(string memberName)
    {
        var match = Regex.Match(
            s_generatedDocs,
            $"""<member name="{Regex.Escape(memberName)}">.*?</member>""",
            RegexOptions.Singleline);
        match.Success.Should().BeTrue(because: $"{memberName} must carry documentation in the shipped XML");
        return match.Value;
    }

    private static bool HasAccurateClassificationPolicy(string doc)
    {
        var normalized = Regex.Replace(doc, @"\s+", " ");
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var text = ContractText(doc);

        // Positive fragments cannot override a later contradictory instruction. Keep these
        // refusals sentence-local so the legitimate general and aggregate rules stay distinct.
        return !Regex.IsMatch(text,
                @"provider[- ]aggregated[^.]*\b(?:must|shall|should)\b[^.]*\b(?:UsOrdinary|UsQualified|Foreign|jurisdictional default)\b"
                + @"|provider[- ]aggregated[^.]*\b(?:must|shall|should)\s+(?:not|never)\b[^.]*Unclassified"
                + @"|Unclassified[^.]*\b(?:must|shall|should)\s+(?:not|never)\b[^.]*provider[- ]aggregated"
                + @"|inference\s+(?:(?:must|shall|should)\s+(?:not|never)\s+be|is not)\s+logged"
                + @"|single payment[^.]*\b(?:must|shall|should)\s+(?:not|never)\b[^.]*jurisdictional default",
                options)
            && Regex.IsMatch(
                normalized,
                @"Provider-aggregated amounts are the sanctioned.{0,200}Unclassified.{0,80}case",
                options)
            && Regex.IsMatch(
                normalized,
                @"adapter emitting a provider-aggregated.{0,200}therefore selects.{0,200}Unclassified",
                options)
            && Regex.IsMatch(normalized, @"inference MUST be logged", options)
            && Regex.IsMatch(
                normalized,
                @"single payment.{0,300}jurisdictional default remains the fallback",
                options);
    }

    [Fact]
    public void DividendPaymentDoc_NoLongerClaimsThereIsNoUnknownSentinel()
    {
        // The enum gained an Unclassified member; the record's Type doc still told adapter authors
        // no such sentinel existed, which reads as "never emit one".
        MemberDoc(DividendPaymentDoc).Should().NotContain(
            "there is intentionally no",
            because: "the sentinel the sentence denies is now a shipped member of the enum");
    }

    [Fact]
    public void DividendTypeDoc_SanctionsProviderAggregatedAmountsAsAnUnclassifiedCase()
    {
        var doc = MemberDoc(DividendTypeDoc);

        HasAccurateClassificationPolicy(doc).Should().BeTrue(
            because: "the packaged policy must relate provider aggregates to Unclassified and require logged inference");
    }

    [Fact]
    public void DividendTypeDoc_StillPrescribesTheJurisdictionalDefaultForSinglePaymentsLackingClassification()
    {
        // Sanctioning the aggregate case must not widen into a general licence to give up on
        // classification: a single payment of unknown category still falls back to the broad
        // jurisdictional category rather than to Unclassified.
        var doc = MemberDoc(DividendTypeDoc);

        HasAccurateClassificationPolicy(doc).Should().BeTrue(
            because: "the packaged policy must retain the jurisdictional default for single unreported payments");
    }

    [Fact]
    public void DividendTypeDocContract_RejectsTheOppositeClassificationPolicy()
    {
        HasAccurateClassificationPolicy(ContradictoryDividendTypePolicy).Should().BeFalse();
    }

    [Theory]
    [InlineData("Override: provider-aggregated dividend amounts MUST be stamped UsOrdinary and MUST NOT use Unclassified.")]
    [InlineData("Provider-aggregated amounts shall use Foreign.")]
    [InlineData("Unclassified must never be used for provider-aggregated amounts.")]
    [InlineData("The inference MUST NOT be logged.")]
    [InlineData("A single payment must not use the jurisdictional default.")]
    public void DividendTypeDocContract_RejectsContradictionsEvenWhenRequiredClaimsRemain(string contradiction)
    {
        HasAccurateClassificationPolicy(MemberDoc(DividendTypeDoc) + contradiction).Should().BeFalse();
    }

    [Theory]
    [InlineData("The MarketData kernel itself derives trailing income yield and consumers must not derive it.")]
    [InlineData("Any overlapping date range can reuse the entry.")]
    public void DividendRequestDocContract_RejectsContradictionsEvenWhenLinkAndRequiredClaimsRemain(string contradiction)
    {
        HasAccurateRequestContract(MemberDoc(DividendHistoryRequestDoc) + contradiction).Should().BeFalse();
    }

    [Theory]
    [InlineData("Consumer boundary:")]
    [InlineData("Cache-key coordinates:")]
    public void DividendRequestDocContract_RejectsMissingBoundaryEvenWhenPublicLinkRemains(string heading)
    {
        var doc = Regex.Replace(MemberDoc(DividendHistoryRequestDoc),
            $"<para><b>{Regex.Escape(heading)}</b>.*?</para>", "", RegexOptions.Singleline);

        HasAccurateRequestContract(doc).Should().BeFalse();
    }

    [Fact]
    public void DividendHistoryDatasetKeyDoc_StatesTheSharedCollisionSafeIdentityBoundary()
    {
        var doc = MemberDoc(DividendHistoryDatasetKeyDoc);

        doc.Should().Contain("injective", because: "opaque dividend symbol lists use a collision-safe encoding");
        doc.Should().Contain("separator-free single-symbol", because: "legacy-safe singleton keys remain stable");
        doc.Should().Contain("slash", because: "opaque ticker separators are escaped before venue qualification");
        doc.Should().NotContain(
            "Formed by exactly the rule",
            because: "an unescaped symbol join no longer describes either request's identity");
        doc.Should().Contain("This matches", because: "dividends and prices now share collision-safe symbol encoding");
    }

    [Fact]
    public void DividendHistoryRequestDoc_PinsConsumerOwnershipExactRangeAndAbsolutePublicGuideLink()
    {
        var doc = MemberDoc(DividendHistoryRequestDoc);
        HasAccurateRequestContract(doc).Should().BeTrue();
    }

    private static bool HasAccurateRequestContract(string doc)
    {
        var text = ContractText(doc);
        const RegexOptions options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        var link = Regex.Match(
            doc,
            "href=\\\"(?<target>[^\\\"]*dividends\\.md)\\\"",
            RegexOptions.CultureInvariant);

        return link.Success
            && Uri.TryCreate(link.Groups["target"].Value, UriKind.Absolute, out var target)
            && target.AbsoluteUri == "https://github.com/boutquin/Boutquin.MarketData/blob/main/docs/dividends.md"
            && Regex.IsMatch(text, @"Deriving an income yield[^.]*is portfolio analytics, not data-kernel work", options)
            && Regex.IsMatch(text, @"snapshot tier keys each dividend entry on the exact requested Range", options)
            && Regex.IsMatch(text, @"Only another dividend request with that same range can reuse the entry", options)
            && Regex.IsMatch(text, @"a price request has a distinct record-type identity", options)
            && !Regex.IsMatch(text,
                @"kernel(?: itself)?\s+(?:derives|computes|calculates)\b[^.]*yield"
                + @"|consumers\s+(?:must|shall|should)\s+(?:not|never)\s+derive"
                + @"|overlapping[^.]*\b(?:can|may|must)\s+reuse",
                options);
    }

    private static string ContractText(string doc)
    {
        // Preserve referenced type/member names when removing XML markup; their fully-qualified
        // cref periods are not sentence boundaries in the prose being checked.
        var references = Regex.Replace(doc, "<see cref=\"[^\"]*[.:](?<name>[^\".:]+)\"\\s*/>", "${name}");
        return Regex.Replace(Regex.Replace(references, "<[^>]+>", " "), @"\s+", " ");
    }

    [Theory]
    [InlineData(DividendPaymentDoc)]
    [InlineData(DividendTypeDoc)]
    public void DividendDocs_CarryNoReferenceToInternalPlanningDocuments(string memberName)
    {
        // The specs folder is stripped from the published repository, so any path, filename or
        // section citation reaching a doc comment is a dead reference for every public consumer.
        var doc = MemberDoc(memberName);

        doc.Should().NotContain("specs/");
        doc.Should().NotContain("§");
        Regex.IsMatch(doc, @"\bfeature-[a-z0-9-]+\.md\b").Should().BeFalse(
            because: $"{memberName} must not cite an internal planning document by filename");
    }
}
