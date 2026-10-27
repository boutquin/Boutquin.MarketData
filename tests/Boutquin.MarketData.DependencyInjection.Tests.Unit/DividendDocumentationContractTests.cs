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

using System.Globalization;
using System.Text.RegularExpressions;

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.Keys;
using Boutquin.MarketData.Orchestration.Pipeline;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// Parses the shipped dividend documentation and compares each of its load-bearing claims against
/// the artifact or source fact the claim is about.
/// </summary>
/// <remarks>
/// <para>
/// Every method here is a pure function over text so that it can be pointed at a falsified document
/// as easily as at the real one. That is the whole design: an assertion nobody has ever seen fail is
/// not evidence, so <see cref="DividendDocumentationContractTests"/> runs each of these against a
/// deliberately-false input as well as the shipped file.
/// </para>
/// <para>
/// The rule these helpers exist to avoid is keyword co-occurrence. Asking whether a document
/// "mentions <c>RegisterDefaultNormalizers</c> and the word false" is satisfied by a document
/// asserting the opposite of the truth; asking which polarity the document *claims* and comparing
/// that to the value the shipped type actually has is not.
/// </para>
/// </remarks>
internal static class DividendDocumentationContract
{
    /// <summary>Phrases that turn an announcement into a denial.</summary>
    private static readonly string[] s_negations =
    [
        "did not", "does not", "do not", "will not", "is not", "are not", "was not", "were not",
        "not add", "no longer", "never", "nothing", "n't", " no ",
    ];

    private static readonly Regex s_defaultPolarityClaim = new(
        @"(?<value>true|false)[`*\s]*by default",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex s_yieldOwnershipClaim = new(
        @"\*\*The (?<owner>consumer|kernel) sums and divides\.\*\*",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex s_relativeMarkdownLink = new(
        @"(?<!!)\[[^\]]+\]\((?<target>[^\s)]+)(?:\s+[^)]*)?\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex s_asOf = new(
        @"As-of date \*\*(?<asof>\d{4}-\d{2}-\d{2})\*\*",
        RegexOptions.CultureInvariant);

    private static readonly Regex s_window = new(
        @"\*\*\((?<open>\d{4}-\d{2}-\d{2}),\s*(?<close>\d{4}-\d{2}-\d{2})\]\*\*",
        RegexOptions.CultureInvariant);

    private static readonly Regex s_exDateRow = new(
        @"^\|\s*(?<date>\d{4}-\d{2}-\d{2})\s*\|\s*(?<amount>[0-9]+\.[0-9]+)\s*\|\s*(?<window>in|out)\b",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex s_statedCash = new(
        @"\*\*Trailing-12M cash\*\*\s*=\s*(?<addends>[0-9.\s+]+?)=\s*\*\*(?<total>[0-9]+\.[0-9]+)\*\*",
        RegexOptions.CultureInvariant);

    private static readonly Regex s_statedPrice = new(
        @"\*\*Price on [^*]*\*\*\s*=\s*\*\*(?<price>[0-9]+\.[0-9]+)\*\*",
        RegexOptions.CultureInvariant);

    private static readonly Regex s_statedYield = new(
        @"\*\*Trailing-12M yield\*\*\s*=\s*(?<numerator>[0-9]+\.[0-9]+)\s*/\s*(?<denominator>[0-9]+\.[0-9]+)"
            + @"\s*=\s*(?<decimalYield>[0-9]+\.[0-9]+)\s*→\s*\*\*(?<percent>[0-9]+\.[0-9]+)\s*%",
        RegexOptions.CultureInvariant);

    private static readonly Regex s_specPath = new(
        @"\bspecs/", RegexOptions.CultureInvariant);

    private static readonly Regex s_sectionCitation = new(
        @"§\s*\d", RegexOptions.CultureInvariant);

    /// <summary>
    /// Standards whose own clause numbering legitimately reaches a public document. A citation of
    /// one of these is a service to the reader; a citation of an internal planning document is a
    /// dead link, because <c>specs/</c> is stripped from the published repository.
    /// </summary>
    private static readonly Regex s_externalStandard = new(
        @"\b(ISDA|ISO|ICMA|IFRS|IAS|GAAP|FpML|RFC|ANSI|ASTM|FIX|SWIFT)\b[^§]*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex s_qualifiedDividendTypeMember = new(
        @"DividendType\.(?<member>[A-Z][A-Za-z0-9]*)",
        RegexOptions.CultureInvariant);

    private static readonly Regex s_dividendTypeParenthetical = new(
        @"`?DividendType`?[^(\n]*\((?<inner>[^)\n]*)\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex s_backtickedIdentifier = new(
        @"`(?<identifier>[A-Z][A-Za-z0-9]*)`",
        RegexOptions.CultureInvariant);

    private static readonly Regex s_boldBullet = new(
        @"^-\s+\*\*(?<lead>.+?)\*\*",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex s_nonPositiveDividendAdmission = new(
        @"(?:(?:zero|negative|non-positive)\s+`?divCash`?\s+values?[^.\n]{0,40}"
            + @"(?:emitted|returned|produced|accepted|payments?))|"
            + @"(?:(?:emit|return|produce|accept)[^.\n]{0,80}(?:zero|negative|non-positive)\s+`?divCash`?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the polarity the document *claims* for <c>RegisterDefaultNormalizers</c>, or
    /// <see langword="null"/> when the document makes no such claim near a mention of the flag.
    /// </summary>
    internal static bool? ReadDefaultNormalizersPolarityClaim(string markdown)
    {
        var flagPositions = Regex.Matches(markdown, "RegisterDefaultNormalizers", RegexOptions.CultureInvariant)
            .Select(m => m.Index)
            .ToList();

        if (flagPositions.Count == 0)
        {
            return null;
        }

        foreach (Match claim in s_defaultPolarityClaim.Matches(markdown))
        {
            // Only a claim in the same breath as the flag is a claim *about* the flag; a
            // "4 decimal places by default" elsewhere in the page is not.
            if (flagPositions.Any(p => claim.Index > p && claim.Index - p <= 400))
            {
                return string.Equals(claim.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase);
            }
        }

        return null;
    }

    /// <summary>
    /// Returns whether the guide says a default host normalizes dividend payments. The default flag
    /// alone is not sufficient: the consumer also needs to know whether a false flag makes the
    /// optional normalizer absent or merely selects a different normalization rule.
    /// </summary>
    internal static bool? ReadDefaultHostNormalizesDividendPaymentsClaim(string markdown)
    {
        markdown = markdown.ReplaceLineEndings("\n");
        const string rawPassThrough = "no normalizer runs on `DividendPayment`, and raw adapter\n"
            + "> amounts reach the caller un-clamped and un-rounded";

        if (markdown.Contains(rawPassThrough, StringComparison.Ordinal))
        {
            return false;
        }

        return markdown.Contains("`DividendPayment` values are normalized before reaching the\n"
                + "> caller", StringComparison.Ordinal)
            ? true
            : null;
    }

    /// <summary>
    /// Returns whether the guide assigns trailing-yield derivation to the consumer. This is a
    /// boundary claim, not a keyword check: the same words can appear in a sentence that assigns
    /// the calculation to the kernel.
    /// </summary>
    internal static bool? ReadConsumerOwnsYieldDerivationClaim(string markdown)
    {
        var claim = s_yieldOwnershipClaim.Match(markdown);
        if (!claim.Success)
        {
            return null;
        }

        return string.Equals(claim.Groups["owner"].Value, "consumer", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether the guide states L2's exact-key cache contract. A subset or superset cache
    /// read is a materially different promise, even though both descriptions mention ranges.
    /// </summary>
    internal static bool? ReadExactRangeCacheClaim(string markdown)
    {
        if (markdown.Contains("serves only exact-key matches", StringComparison.Ordinal)
            && markdown.Contains("**exact** requested `(From, To)`", StringComparison.Ordinal))
        {
            return true;
        }

        return markdown.Contains("subset or superset", StringComparison.Ordinal)
            || markdown.Contains("overlapping `(From, To)`", StringComparison.Ordinal)
            ? false
            : null;
    }

    /// <summary>
    /// Returns whether the guide incorrectly makes a dividend-cache hit depend on issuing a
    /// covering price request with identical bounds. Price and dividend snapshots carry different
    /// record-type prefixes, so range alignment is an analytical choice rather than cache identity.
    /// </summary>
    internal static bool ClaimsPriceRangeControlsDividendCacheReuse(string markdown) =>
        markdown.Contains("dividend fetch and the covering price fetch over identical range coordinates", StringComparison.Ordinal)
        && markdown.ReplaceLineEndings("\n").Contains("the dividend fetch will go to\n"
            + "the provider every time", StringComparison.Ordinal);

    /// <summary>
    /// Returns whether the guide makes an unconditional normalized-output promise at the ownership
    /// boundary, independently of its later and more specific default-host composition guidance.
    /// </summary>
    internal static bool ClaimsKernelAlwaysReturnsNormalizedDividendPayments(string markdown) =>
        markdown.Contains("**The kernel returns normalized `DividendPayment` records**", StringComparison.Ordinal);

    internal static bool HasAccurateDividendBatchCacheOrder(string markdown) =>
        markdown.Contains("time-series path", StringComparison.OrdinalIgnoreCase)
        && markdown.Contains("aggregate request", StringComparison.OrdinalIgnoreCase)
        && !markdown.Contains("before it reaches an adapter or a cache key", StringComparison.Ordinal);

    internal static bool HasPositiveOnlyDividendCashAcceptanceCriterion(string markdown) =>
        Regex.Match(markdown, @"^.*AC-3\.2.*$", RegexOptions.Multiline | RegexOptions.CultureInvariant).Value is var ac32
        && ac32.Contains("positive `divCash`", StringComparison.Ordinal)
        && !ac32.Contains("non-zero `divCash`", StringComparison.Ordinal);

    internal static bool HasDividendSpecificChangelogIdentity(string markdown) =>
        markdown.Contains("collision-safe symbol-list encoding", StringComparison.Ordinal)
        && !markdown.Contains("same symbol-join and exchange-suffix `DatasetKey` rule", StringComparison.Ordinal);

    internal static bool HasPositiveOnlyDividendCashClaim(string markdown) =>
        markdown.Contains("positive `divCash`", StringComparison.Ordinal)
        && !markdown.Contains("non-zero `divCash`", StringComparison.Ordinal)
        && Regex.IsMatch(
            markdown,
            @"\bnone\s+for\s+zero\s+or\s+negative\s+values\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
        && !s_nonPositiveDividendAdmission.IsMatch(markdown)
        && markdown.Contains("one canonical `DividendPayment` per unique ex-date", StringComparison.Ordinal)
        && markdown.Contains("duplicate provider rows", StringComparison.Ordinal);

    /// <summary>
    /// Returns whether the active ship recipe is pinned to the committed v2 source and keeps build
    /// artifacts, build restore state, and the cold-consumer restore state independently isolated.
    /// </summary>
    internal static bool HasV2SafeShipRecipe(string markdown) =>
        markdown.Contains("VERSION=\"2.0.0-local.${BUILD_DATE}.sha${COMMIT_SHORT}\"", StringComparison.Ordinal)
        && markdown.Contains("ASSEMBLY_VERSION=2.0.0.0", StringComparison.Ordinal)
        && markdown.Contains("FILE_VERSION=2.0.0.0", StringComparison.Ordinal)
        && markdown.Contains("git clone --no-local", StringComparison.Ordinal)
        && markdown.Contains("checkout --detach \"$KERNEL_COMMIT\"", StringComparison.Ordinal)
        && markdown.Contains("--artifacts-path \"$KERNEL_ARTIFACTS\"", StringComparison.Ordinal)
        && markdown.Contains("BUILD_PACKAGES=", StringComparison.Ordinal)
        && markdown.Contains("CONSUMER_PACKAGES=", StringComparison.Ordinal)
        && markdown.Contains("--packages \"$BUILD_PACKAGES\"", StringComparison.Ordinal)
        && markdown.Contains("--packages \"$CONSUMER_PACKAGES\"", StringComparison.Ordinal);

    /// <summary>
    /// Returns the executable ship-recipe commands that do not preserve a single restore/build
    /// context. A restore into the default artifact root followed by a <c>--no-restore</c> build in
    /// an isolated artifact root fails with <c>NETSDK1004</c>, even when both commands are otherwise
    /// valid in isolation.
    /// </summary>
    internal static IReadOnlyList<string> FindExecutableShipRecipeDefects(string markdown)
    {
        var normalized = Regex.Replace(
            markdown.ReplaceLineEndings("\n").Replace("\\\n", " ", StringComparison.Ordinal),
            @"[ \t]+",
            " ");
        const string versionProperties = "/p:MinVerVersionOverride=\"$VERSION\" "
            + "/p:AssemblyVersion=\"$ASSEMBLY_VERSION\" /p:FileVersion=\"$FILE_VERSION\"";

        (string Name, string Command)[] requiredCommands =
        [
            (
                "kernel restore",
                "NUGET_PACKAGES=\"$BUILD_PACKAGES\" /usr/local/share/dotnet/dotnet restore "
                    + "Boutquin.MarketData.slnx --configfile \"$CONFIG\" --packages \"$BUILD_PACKAGES\" "
                    + "--artifacts-path \"$KERNEL_ARTIFACTS\" " + versionProperties),
            (
                "kernel build",
                "NUGET_PACKAGES=\"$BUILD_PACKAGES\" /usr/local/share/dotnet/dotnet build "
                    + "Boutquin.MarketData.slnx --configuration Release --no-restore "
                    + "--artifacts-path \"$KERNEL_ARTIFACTS\" " + versionProperties),
            (
                "kernel pack",
                "NUGET_PACKAGES=\"$BUILD_PACKAGES\" /usr/local/share/dotnet/dotnet pack \"$project\" "
                    + "--configuration Release --no-build --no-restore --artifacts-path \"$KERNEL_ARTIFACTS\" "
                    + versionProperties),
            (
                "Adapter restore",
                "NUGET_PACKAGES=\"$BUILD_PACKAGES\" /usr/local/share/dotnet/dotnet restore "
                    + "Boutquin.MarketData.Adapter.slnx --configfile \"$CONFIG\" --packages \"$BUILD_PACKAGES\" "
                    + "--artifacts-path \"$ADAPTER_ARTIFACTS\" " + versionProperties),
            (
                "Adapter build",
                "NUGET_PACKAGES=\"$BUILD_PACKAGES\" /usr/local/share/dotnet/dotnet build "
                    + "Boutquin.MarketData.Adapter.slnx --configuration Release --no-restore "
                    + "--artifacts-path \"$ADAPTER_ARTIFACTS\" " + versionProperties),
            (
                "Adapter Shared pack",
                "NUGET_PACKAGES=\"$BUILD_PACKAGES\" /usr/local/share/dotnet/dotnet pack "
                    + "src/Shared/Shared.csproj --configuration Release --no-build --no-restore "
                    + "--artifacts-path \"$ADAPTER_ARTIFACTS\" " + versionProperties),
            (
                "Adapter Tiingo pack",
                "NUGET_PACKAGES=\"$BUILD_PACKAGES\" /usr/local/share/dotnet/dotnet pack "
                    + "src/Tiingo/Tiingo.csproj --configuration Release --no-build --no-restore "
                    + "--artifacts-path \"$ADAPTER_ARTIFACTS\" " + versionProperties),
            (
                "cold-consumer restore",
                "NUGET_PACKAGES=\"$CONSUMER_PACKAGES\" /usr/local/share/dotnet/dotnet restore "
                    + "\"$CONSUMER/Consumer.csproj\" --configfile \"$CONFIG\" --packages \"$CONSUMER_PACKAGES\""),
            (
                "cold-consumer build",
                "NUGET_PACKAGES=\"$CONSUMER_PACKAGES\" /usr/local/share/dotnet/dotnet build "
                    + "\"$CONSUMER/Consumer.csproj\" --configuration Release --no-restore"),
        ];

        return requiredCommands
            .Where(required => !normalized.Contains(required.Command, StringComparison.Ordinal))
            .Select(required => required.Name)
            .ToList();
    }

    /// <summary>The worked trailing-12M example, re-derived from the document's own table.</summary>
    internal sealed record WorkedExample(
        DateOnly AsOf,
        DateOnly WindowOpen,
        DateOnly WindowClose,
        IReadOnlyList<(DateOnly Date, decimal Amount, bool InWindow)> Rows,
        IReadOnlyList<decimal> StatedAddends,
        decimal StatedCash,
        decimal StatedPrice,
        decimal PrintedNumerator,
        decimal PrintedDenominator,
        decimal StatedDecimalYield,
        decimal StatedPercentYield);

    /// <summary>
    /// Extracts the worked example, or returns <see langword="null"/> when the document does not
    /// carry one in a shape that can be recomputed at all — which is itself a failure, since an
    /// example nobody can check is not evidence of the arithmetic it claims.
    /// </summary>
    internal static WorkedExample? ParseWorkedExample(string markdown)
    {
        var asOf = s_asOf.Match(markdown);
        var window = s_window.Match(markdown);
        var cash = s_statedCash.Match(markdown);
        var price = s_statedPrice.Match(markdown);
        var yield = s_statedYield.Match(markdown);

        if (!asOf.Success || !window.Success || !cash.Success || !price.Success || !yield.Success)
        {
            return null;
        }

        var rows = s_exDateRow.Matches(markdown)
            .Select(m => (
                Date: DateOnly.ParseExact(m.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Amount: decimal.Parse(m.Groups["amount"].Value, CultureInfo.InvariantCulture),
                InWindow: m.Groups["window"].Value == "in"))
            .ToList();

        if (rows.Count == 0)
        {
            return null;
        }

        var addends = cash.Groups["addends"].Value
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => decimal.Parse(a, CultureInfo.InvariantCulture))
            .ToList();

        return new WorkedExample(
            DateOnly.ParseExact(asOf.Groups["asof"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(window.Groups["open"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly.ParseExact(window.Groups["close"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            rows,
            addends,
            decimal.Parse(cash.Groups["total"].Value, CultureInfo.InvariantCulture),
            decimal.Parse(price.Groups["price"].Value, CultureInfo.InvariantCulture),
            decimal.Parse(yield.Groups["numerator"].Value, CultureInfo.InvariantCulture),
            decimal.Parse(yield.Groups["denominator"].Value, CultureInfo.InvariantCulture),
            decimal.Parse(yield.Groups["decimalYield"].Value, CultureInfo.InvariantCulture),
            decimal.Parse(yield.Groups["percent"].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Every way the example can disagree with itself, as a list of human-readable defects. Empty
    /// means the arithmetic, the addends, and the in/out labels are all recomputable and correct.
    /// </summary>
    internal static IReadOnlyList<string> FindWorkedExampleDefects(WorkedExample example)
    {
        var defects = new List<string>();

        if (example.WindowClose != example.AsOf)
        {
            defects.Add($"the printed window close is {example.WindowClose:yyyy-MM-dd} but the as-of date is "
                + $"{example.AsOf:yyyy-MM-dd}");
        }

        var inWindow = example.Rows.Where(r => r.InWindow).Select(r => r.Amount).ToList();

        if (inWindow.Count < 3)
        {
            defects.Add($"the example lists {inWindow.Count} in-window ex-dates; a trailing-12M "
                + "illustration needs at least three to be worth reading");
        }

        var recomputed = inWindow.Sum();
        if (recomputed != example.StatedCash)
        {
            defects.Add($"in-window rows sum to {recomputed} but the stated trailing-12M cash is {example.StatedCash}");
        }

        if (!example.StatedAddends.OrderBy(a => a).SequenceEqual(inWindow.OrderBy(a => a)))
        {
            defects.Add($"the stated addends [{string.Join(", ", example.StatedAddends)}] are not the "
                + $"in-window amounts [{string.Join(", ", inWindow)}]");
        }

        if (example.StatedPrice == 0m)
        {
            defects.Add("the stated as-of price is zero");
        }
        else
        {
            if (example.PrintedNumerator != example.StatedCash)
            {
                defects.Add($"the printed numerator is {example.PrintedNumerator} but the stated cash is "
                    + $"{example.StatedCash}");
            }

            if (example.PrintedDenominator != example.StatedPrice)
            {
                defects.Add($"the printed denominator is {example.PrintedDenominator} but the stated price is "
                    + $"{example.StatedPrice}");
            }

            var recomputedYield = Math.Round(example.StatedCash / example.StatedPrice, 6, MidpointRounding.AwayFromZero);
            if (recomputedYield != example.StatedDecimalYield)
            {
                defects.Add($"{example.StatedCash} / {example.StatedPrice} recomputes to {recomputedYield} "
                    + $"but the document states {example.StatedDecimalYield}");
            }

            var recomputedPercent = Math.Round(example.StatedDecimalYield * 100m, 2, MidpointRounding.AwayFromZero);
            if (recomputedPercent != example.StatedPercentYield)
            {
                defects.Add($"{example.StatedDecimalYield} is {recomputedPercent}% but the document "
                    + $"states {example.StatedPercentYield}%");
            }
        }

        if (example.WindowOpen != example.AsOf.AddDays(-365))
        {
            defects.Add($"the stated window opens at {example.WindowOpen}, which is not "
                + $"{example.AsOf}.AddDays(-365)");
        }

        foreach (var row in example.Rows)
        {
            var belongs = row.Date > example.WindowOpen && row.Date <= example.AsOf;
            if (belongs != row.InWindow)
            {
                defects.Add($"{row.Date:yyyy-MM-dd} is labelled '{(row.InWindow ? "in" : "out")}' but the "
                    + $"half-open window ({example.WindowOpen:yyyy-MM-dd}, {example.AsOf:yyyy-MM-dd}] "
                    + $"puts it '{(belongs ? "in" : "out")}'");
            }
        }

        return defects;
    }

    /// <summary>
    /// References to the gitignored planning folder, which 404 for every public consumer because
    /// <c>specs/</c> is stripped from the release repository.
    /// </summary>
    /// <param name="text">The shipped file's full text.</param>
    /// <param name="relativePath">The shipped file's path relative to the repository root.</param>
    /// <param name="repositoryRoot">The repository root used to resolve relative Markdown links.</param>
    /// <param name="allowExternalStandardCitations">
    /// When <see langword="true"/>, a <c>§</c> introduced by a named external standard (ISDA 2006
    /// §4.16(f) and friends) is a legitimate citation rather than an internal spec reference.
    /// </param>
    internal static IReadOnlyList<string> FindInternalPlanningReferences(
        string text,
        string relativePath,
        string repositoryRoot,
        bool allowExternalStandardCitations)
    {
        var violations = new List<string>();

        violations.AddRange(s_specPath.Matches(text).Select(m => $"specs/ path at offset {m.Index}"));

        foreach (Match citation in s_sectionCitation.Matches(text))
        {
            var lineStart = text.LastIndexOf('\n', Math.Max(citation.Index - 1, 0)) + 1;
            var prefix = text[lineStart..citation.Index];

            if (allowExternalStandardCitations && s_externalStandard.IsMatch(prefix))
            {
                continue;
            }

            violations.Add($"section citation '{citation.Value.Trim()}' at offset {citation.Index}");
        }

        var canonicalRepositoryRoot = ResolveExistingPath(repositoryRoot);
        var publicDocsRoot = ResolveExistingPath(Path.Combine(repositoryRoot, "docs"));
        var sourceDirectory = Path.GetDirectoryName(relativePath) ?? string.Empty;

        foreach (Match link in s_relativeMarkdownLink.Matches(text))
        {
            var target = link.Groups["target"].Value;
            if (target.StartsWith('#')
                || Uri.TryCreate(target, UriKind.Absolute, out _))
            {
                continue;
            }

            var pathOnly = target.Split(['#', '?'], 2)[0];
            var resolved = Path.GetFullPath(Path.Combine(repositoryRoot, sourceDirectory, pathOnly));
            var exists = File.Exists(resolved);
            var canonicalResolved = exists ? ResolveExistingPath(resolved) : resolved;
            var isPublicDoc = exists && IsDescendantOf(canonicalResolved, publicDocsRoot);
            var isPublicRootFile = exists
                && string.Equals(
                    Path.GetDirectoryName(canonicalResolved),
                    canonicalRepositoryRoot,
                    StringComparison.Ordinal);

            if (!isPublicDoc && !isPublicRootFile)
            {
                violations.Add($"non-public relative link '{target}' at offset {link.Index}");
            }
        }

        return violations;
    }

    internal static string ResolveExistingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new InvalidOperationException($"Path '{path}' has no root.");
        var current = root;

        foreach (var segment in Path.GetRelativePath(root, fullPath)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(next)
                ? new DirectoryInfo(next)
                : new FileInfo(next);
            current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next;
        }

        return Path.GetFullPath(current);
    }

    private static bool IsDescendantOf(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !string.Equals(relative, ".", StringComparison.Ordinal);
    }

    /// <summary>
    /// Identifiers the document presents as members of <c>DividendType</c> — both qualified
    /// (<c>DividendType.Foreign</c>) and enumerated in a parenthetical that follows the type name.
    /// </summary>
    internal static IReadOnlyList<string> FindCitedDividendTypeMembers(string markdown)
    {
        var cited = new List<string>();

        cited.AddRange(s_qualifiedDividendTypeMember.Matches(markdown)
            .Select(m => m.Groups["member"].Value));

        foreach (Match parenthetical in s_dividendTypeParenthetical.Matches(markdown))
        {
            cited.AddRange(s_backtickedIdentifier.Matches(parenthetical.Groups["inner"].Value)
                .Select(m => m.Groups["identifier"].Value));
        }

        return cited.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The bold lead-in of every bullet in the named subsection of a changelog heading.</summary>
    internal static IReadOnlyList<string> ReadBoldBulletLeads(
        string changelog,
        string versionHeading,
        string subsection)
    {
        changelog = changelog.ReplaceLineEndings("\n");
        var versionStart = changelog.IndexOf($"## {versionHeading}", StringComparison.Ordinal);
        if (versionStart < 0)
        {
            return [];
        }

        var versionEnd = changelog.IndexOf("\n## ", versionStart + 1, StringComparison.Ordinal);
        if (versionEnd >= 0)
        {
            changelog = changelog[..versionEnd];
        }

        var subsectionStart = changelog.IndexOf($"### {subsection}", versionStart, StringComparison.Ordinal);
        if (subsectionStart < 0)
        {
            return [];
        }

        var subsectionEnd = changelog.IndexOf("\n### ", subsectionStart + 1, StringComparison.Ordinal);
        var block = subsectionEnd < 0
            ? changelog[subsectionStart..]
            : changelog[subsectionStart..subsectionEnd];

        return s_boldBullet.Matches(block).Select(m => m.Groups["lead"].Value).ToList();
    }

    /// <summary>
    /// <see langword="true"/> when the text announces the deliverable rather than denying it. A
    /// bullet reading "we did NOT add X" names X just as literally as one that ships it.
    /// </summary>
    internal static bool AnnouncesAffirmatively(string text, string deliverable)
    {
        if (!text.Contains(deliverable, StringComparison.Ordinal))
        {
            return false;
        }

        var padded = $" {text.ToLowerInvariant()} ";
        return !s_negations.Any(n => padded.Contains(n, StringComparison.Ordinal));
    }

    internal static bool HasCanonicalDividendSource(string markdown)
    {
        var row = FindTableRow(markdown, "DividendHistoryRequest");
        var providers = row is { Count: >= 3 }
            ? row[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(provider => provider.Trim('`'))
                .ToList()
            : [];

        return row is { Count: >= 3 }
            && row[1] == "`DividendPayment`"
            && providers.SequenceEqual(["Tiingo"], StringComparer.Ordinal)
            && AnnouncesAffirmatively(string.Join(" | ", row), "DividendPayment");
    }

    /// <summary>A row of a GitHub-flavoured markdown table, split into trimmed cells.</summary>
    internal static IReadOnlyList<string>? FindTableRow(string markdown, string firstCellContains)
    {
        foreach (var line in markdown.Split('\n'))
        {
            if (!line.StartsWith('|'))
            {
                continue;
            }

            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count > 1 && cells[0].Contains(firstCellContains, StringComparison.Ordinal))
            {
                return cells;
            }
        }

        return null;
    }
}

/// <summary>
/// Pins the shipped dividend documentation to the code it describes, and pins the checker itself
/// to a falsified input so that a green here means something.
/// </summary>
/// <remarks>
/// <para>
/// Documentation rots silently: no compiler and no runtime test notices when a page keeps asserting
/// a default that flipped, or keeps citing an enum member that was never added. These assertions are
/// the compiled replacement for a review pass nobody re-runs.
/// </para>
/// <para>
/// Each shipped-file assertion is paired with a discrimination test that feeds the same helper a
/// deliberately-false document and requires it to object. A checker that cannot fail is not a gate,
/// and a green from one carries no information — so the discrimination tests are load-bearing, not
/// decorative.
/// </para>
/// </remarks>
public sealed partial class DividendDocumentationContractTests
{
    private const string DividendHistoryRequestMember =
        "T:Boutquin.MarketData.Abstractions.Requests.DividendHistoryRequest";

    private const string DividendsDoc = "docs/dividends.md";
    private const string ReferenceDataDoc = "docs/reference-data.md";
    private const string Changelog = "CHANGELOG.md";
    private const string Readme = "README.md";
    private const string RequestSource = "src/Boutquin.MarketData.Abstractions/Requests/DividendHistoryRequest.cs";

    /// <summary>A document that states the opposite of the shipped default, in the same vocabulary.</summary>
    private const string DocumentInvertingTheDefault = """
        ## Registering the normalizer

        Registration: you do NOT need INormalizer or DividendPaymentNormalizer; the kernel wires it
        for you. RegisterDefaultNormalizers is true by default, not false, so normalizers always
        resolve.
        """;

    /// <summary>A worked example whose stated total disagrees with the table above it.</summary>
    private const string ExampleDisagreeingWithItsOwnTable = """
        As-of date **2026-06-30**, symbol **SCHD**, trailing window
        **(2025-06-30, 2026-06-30]** — half-open.

        | Ex-date | Amount (USD) | Window |
        |---|---|---|
        | 2025-09-24 | 0.2645 | in |
        | 2025-12-10 | 0.2712 | in |
        | 2026-03-25 | 0.2489 | in |
        | 2026-06-24 | 0.2803 | in |

        **Trailing-12M cash** = 0.2645 + 0.2712 + 0.2489 + 0.2803 = **1.5000**

        **Price on 2026-06-30** = **27.50**

        **Trailing-12M yield** = 1.5000 / 27.50 = 0.054545 → **5.45 %**
        """;

    private const string ExampleWithUnrelatedPrintedOperands = """
        As-of date **2026-06-30**, symbol **SCHD**, trailing window
        **(2025-06-30, 2026-06-30]** — half-open.

        | Ex-date | Amount (USD) | Window |
        |---|---|---|
        | 2025-09-24 | 0.2645 | in |
        | 2025-12-10 | 0.2712 | in |
        | 2026-03-25 | 0.2489 | in |
        | 2026-06-24 | 0.2803 | in |

        **Trailing-12M cash** = 0.2645 + 0.2712 + 0.2489 + 0.2803 = **1.0649**

        **Price on 2026-06-30** = **27.50**

        **Trailing-12M yield** = 9.9999 / 88.88 = 0.038724 → **3.87 %**
        """;

    private const string ExampleWhoseWindowClosesAfterItsAsOfDate = """
        As-of date **2026-06-30**, symbol **SCHD**, trailing window
        **(2025-06-30, 2027-06-30]** — half-open.

        | Ex-date | Amount (USD) | Window |
        |---|---|---|
        | 2025-09-24 | 0.2645 | in |
        | 2025-12-10 | 0.2712 | in |
        | 2026-03-25 | 0.2489 | in |
        | 2026-06-24 | 0.2803 | in |

        **Trailing-12M cash** = 0.2645 + 0.2712 + 0.2489 + 0.2803 = **1.0649**

        **Price on 2026-06-30** = **27.50**

        **Trailing-12M yield** = 1.0649 / 27.50 = 0.038724 → **3.87 %**
        """;

    private const string ChangelogDenyingItsDeliverables = """
        ## [Unreleased]

        ### Added
        - **We did not add `DividendHistoryRequest` in this release.** Nothing ships.
        - **No Tiingo dividend adapter ships.** See specs/feature-dividend-history-wiring.md §2.5.
        """;

    private const string ChangelogDescribingNegativeDivCashAsAPayment = """
        ## [Unreleased]

        ### Added
        - **Tiingo dividends.** The Tiingo adapter emits a payment for each bar with a non-zero `divCash` value.
        """;

    private const string ChangelogPromisingOnePaymentPerPositiveBar = """
        ## [Unreleased]

        ### Added
        - **Tiingo dividends.** It emits one `DividendPayment` per bar carrying a positive `divCash`.
        """;

    private const string ChangelogContradictingItsPositiveOnlyClaim = """
        ## [Unreleased]

        ### Added
        - **Tiingo dividend adapter.** It emits one canonical `DividendPayment` per unique ex-date carrying a positive `divCash`; identical duplicate provider rows collapse to that single payment. Negative `divCash` values are also emitted as payments.
        """;

    private const string DocumentAssigningYieldToTheKernel = """
        ## Ownership boundary

        - **The kernel sums and divides.** The trailing-yield derivation belongs in MarketData.
        """;

    private const string DocumentPromisingOverlappingRangeReuse = """
        ## Cache keys

        The L2 snapshot tier reuses an overlapping `(From, To)` range when a request is a subset or superset.
        """;

    private const string DocumentClaimingPriceRangeControlsDividendCacheReuse = """
        ## Cache keys are exact-range

        This matters most when pairing dividends with prices for a yield: issue the
        dividend fetch and the covering price fetch over identical range coordinates, or
        the two will occupy unrelated cache entries and the dividend fetch will go to
        the provider every time.
        """;

    private const string DocumentUnconditionallyPromisingNormalizedKernelOutput = """
        ## Ownership boundary

        - **The kernel returns normalized `DividendPayment` records** for a `(symbol, range)`.

        ## Registering the normalizer

        `RegisterDefaultNormalizers` is false by default. In a default host, no normalizer runs.
        """;

    private const string StaleV1ShipRecipe = """
        VERSION=1.2.0-local
        FILE_VERSION=1.2.0.0
        PACKAGES="$SHIP_ROOT/packages"
        dotnet build /p:AssemblyVersion=1.0.0.0
        dotnet restore --packages "$PACKAGES"
        """;

    private const string DocumentClaimingDefaultHostNormalization = """
        ## Registering the normalizer

        `RegisterDefaultNormalizers` is false by default.

        > In a default host, `DividendPayment` values are normalized before reaching the
        > caller, including clamping and rounding.
        """;

    private const string DocumentLinkingPrivatePlan = """
        ## Related

        See the [internal cutover plan](planning/dividend-cutover.md).
        """;

    private const string ReadmeClaimingTwelveDataIsTheExclusiveDividendSource = """
        | Request | Record | Source |
        |---|---|---|
        | `DividendHistoryRequest` | `DividendPayment` | TwelveData exclusively; historical Tiingo reference |
        """;

    private static string RepoRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("CODE_ARENA_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot)
            && File.Exists(Path.Combine(configuredRoot, "Boutquin.MarketData.slnx")))
        {
            return configuredRoot;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Boutquin.MarketData.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull(
            because: $"the repository root carries Boutquin.MarketData.slnx; searched upward from {AppContext.BaseDirectory}");
        return directory!.FullName;
    }

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), relativePath);
        File.Exists(path).Should().BeTrue(because: $"{relativePath} is a shipped file; looked in {path}");
        return File.ReadAllText(path);
    }

    private static string GeneratedAbstractionsXml()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Boutquin.MarketData.Abstractions.xml");
        File.Exists(path).Should().BeTrue(
            because: $"the Abstractions package emits its documentation file beside the assembly; looked in {path}");
        return File.ReadAllText(path);
    }

    private static string GeneratedCacheXml()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Boutquin.MarketData.Cache.xml");
        File.Exists(path).Should().BeTrue(
            because: $"the Cache package emits its documentation file beside the assembly; looked in {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void DividendsDoc_EmptySuccessRequiresCompleteCoverage()
    {
        var guide = ReadRepoFile(DividendsDoc);
        var start = guide.IndexOf("## A window with no ex-dates is an empty success", StringComparison.Ordinal);
        var end = guide.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var section = guide[start..(end < 0 ? guide.Length : end)];

        section.Should().Contain("`Complete`", because: "only a fully observed empty window establishes zero dividends");
        section.Should().Contain("`Partial`", because: "incomplete empty coverage must not be interpreted as zero");
        section.Should().NotContain("not `Missing`", because: "that predicate also admits Partial coverage");
    }

    [Fact]
    public void GeneratedAbstractionsXml_DividendHistoryRequest_CrossReferencesTheDividendsDoc()
    {
        // The generated XML — not the source comment — is what reaches a consumer's IntelliSense
        // inside the package, so that is where the cross-reference has to survive.
        var member = Regex.Match(
            GeneratedAbstractionsXml(),
            $"""<member name="{Regex.Escape(DividendHistoryRequestMember)}">.*?</member>""",
            RegexOptions.Singleline);

        member.Success.Should().BeTrue(
            because: "DividendHistoryRequest must carry documentation in the shipped XML");
        member.Value.Should().Contain(
            DividendsDoc,
            because: "the request's remarks are the only place a consumer is pointed at the "
                + "ownership boundary before they go looking for a yield method that does not exist");
    }

    [Fact]
    public void Changelog_DividendRequest_StatesItsCollisionSafeIdentityBoundary()
    {
        DividendDocumentationContract.HasDividendSpecificChangelogIdentity(ReadRepoFile("CHANGELOG.md")).Should().BeTrue();
    }

    [Fact]
    public void DividendsDoc_SeparatesTimeSeriesSplitKeysFromLegacyAggregateCacheIdentity()
    {
        DividendDocumentationContract.HasAccurateDividendBatchCacheOrder(ReadRepoFile(DividendsDoc)).Should().BeTrue();
    }

    [Fact]
    public void DividendsDoc_WorkedExample_RecomputesFromItsOwnTable()
    {
        var example = DividendDocumentationContract.ParseWorkedExample(ReadRepoFile(DividendsDoc));

        example.Should().NotBeNull(
            because: "the worked trailing-12M example must be laid out so a reader — and this test — "
                + "can recompute it from the document's own ex-date table");
        DividendDocumentationContract.FindWorkedExampleDefects(example!).Should().BeEmpty();
    }

    [Fact]
    public void DividendsDoc_DefaultNormalizerClaim_MatchesTheShippedRuntimeDefault()
    {
        // The source fact, read off the shipped type rather than off the page describing it.
        var shippedDefault = new DataPipelineOptions().RegisterDefaultNormalizers;

        var claimed = DividendDocumentationContract.ReadDefaultNormalizersPolarityClaim(
            ReadRepoFile(DividendsDoc));

        claimed.Should().NotBeNull(
            because: "the page tells hosts whether they must register DividendPaymentNormalizer "
                + "themselves, so it has to state the flag's default explicitly");
        claimed!.Value.Should().Be(
            shippedDefault,
            because: $"DataPipelineOptions.RegisterDefaultNormalizers is {shippedDefault} on a "
                + "freshly-constructed options instance, and a page that claims the other polarity "
                + "sends every host down the wrong composition path");
    }

    [Fact]
    public void DividendsDoc_DefaultHostNormalizationClaim_MatchesTheShippedResolutionBehavior()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel();
        using var provider = services.BuildServiceProvider();

        var normalizer = provider.GetService<INormalizer<DividendPayment, DividendPayment>>();
        var claimed = DividendDocumentationContract.ReadDefaultHostNormalizesDividendPaymentsClaim(
            ReadRepoFile(DividendsDoc));

        claimed.Should().NotBeNull(
            because: "the guide must state whether the default host changes dividend payment amounts");
        claimed.Should().Be(
            normalizer is not null,
            because: "the default registration factory returns null when RegisterDefaultNormalizers is false, "
                + "so an unconfigured host passes raw adapter amounts through");
        DividendDocumentationContract
            .ClaimsKernelAlwaysReturnsNormalizedDividendPayments(ReadRepoFile(DividendsDoc))
            .Should().BeFalse(
                because: "an unconditional normalized-output headline contradicts the observed default-host raw path");
    }

    [Fact]
    public void DividendsDoc_YieldOwnershipClaim_MatchesTheShippedPublicApiBoundary()
    {
        var kernelAssemblies = new[]
        {
            typeof(DividendHistoryRequest).Assembly,
            typeof(DataPipeline).Assembly,
            typeof(ServiceCollectionExtensions).Assembly,
        };
        var trailingYieldApi = kernelAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(type => type.GetMethods())
            .Where(method => string.Equals(method.Name, "GetTrailingYieldAsync", StringComparison.Ordinal))
            .ToList();

        trailingYieldApi.Should().BeEmpty(
            because: "the kernel exposes raw dividend payments rather than a trailing-yield calculation API");
        DividendDocumentationContract.ReadConsumerOwnsYieldDerivationClaim(ReadRepoFile(DividendsDoc))
            .Should().BeTrue(
                because: "the absence of a kernel trailing-yield API leaves the consumer to sum payments and "
                    + "divide by its own as-of price");
    }

    [Fact]
    public void DividendsDoc_ExactRangeCacheClaim_MatchesTheShippedCacheKeyIdentity()
    {
        var requested = CacheKeyBuilder.FromDateRange(
            "DividendPayment", "SCHD", new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30));
        var repeated = CacheKeyBuilder.FromDateRange(
            "DividendPayment", "SCHD", new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30));
        var narrowed = CacheKeyBuilder.FromDateRange(
            "DividendPayment", "SCHD", new DateOnly(2025, 7, 2), new DateOnly(2026, 6, 30));
        var sameRangePrice = CacheKeyBuilder.FromDateRange(
            "Bar", "SCHD", new DateOnly(2025, 7, 1), new DateOnly(2026, 6, 30));
        var differentlyRangedPrice = CacheKeyBuilder.FromDateRange(
            "Bar", "SCHD", new DateOnly(2024, 1, 1), new DateOnly(2026, 6, 30));

        requested.Should().Be(repeated, because: "repeating identical dividend bounds reuses the dividend key");
        requested.Should().NotBe(narrowed, because: "the legacy L2 cache identity includes both requested range bounds");
        requested.Should().NotBe(sameRangePrice,
            because: "record type is part of cache identity, so a price snapshot is never a dividend snapshot");
        requested.Should().NotBe(differentlyRangedPrice,
            because: "a price request's bounds do not participate in the dividend request's key");
        DividendDocumentationContract.ReadExactRangeCacheClaim(ReadRepoFile(DividendsDoc))
            .Should().BeTrue(
                because: "different range coordinates produce different L2 keys rather than a subset or superset read");
        DividendDocumentationContract.ClaimsPriceRangeControlsDividendCacheReuse(ReadRepoFile(DividendsDoc))
            .Should().BeFalse(
                because: "matching price bounds is an analytical alignment choice, not a cause of dividend-cache reuse");
    }

    [Fact]
    public void GeneratedCacheXml_DividendBuilder_DescribesVenueInsideTheEncodedTickerCoordinate()
    {
        const string memberName =
            "T:Boutquin.MarketData.Cache.TimeSeries.DividendPaymentDatasetKeyBuilder";
        var member = Regex.Match(
            GeneratedCacheXml(),
            $"""<member name="{Regex.Escape(memberName)}">.*?</member>""",
            RegexOptions.Singleline);

        member.Success.Should().BeTrue(because: "the public builder contract must ship in generated XML");
        member.Value.Should().Contain("ticker coordinate");
        member.Value.Should().Contain("escaped");
        member.Value.Should().Contain("venue-qualified");
        member.Value.Should().NotContain(
            "optional venue column",
            because: "the two-field wire encodes venue inside Ticker rather than adding a physical column");
    }

    /// <summary>
    /// Every file that ships to the public release repository and is in this milestone's reach.
    /// Enumerated from disk rather than listed, so a documentation page added later is gated the
    /// day it appears instead of the day someone remembers to add it here.
    /// </summary>
    public static TheoryData<string> ShippedPublicFiles()
    {
        var data = new TheoryData<string>();
        var root = RepoRoot();

        foreach (var page in Directory.EnumerateFiles(Path.Combine(root, "docs"), "*.md").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetRelativePath(root, page));
        }

        data.Add(Changelog);
        data.Add(Readme);
        data.Add(RequestSource);
        return data;
    }

    [Theory]
    [MemberData(nameof(ShippedPublicFiles))]
    public void ShippedPublicFiles_CiteNoInternalPlanningDocument(string relativePath)
    {
        // specs/ is gitignored out of the public release repository, so any path, filename or bare
        // section citation reaching a shipped file is a dead link for every public consumer.
        DividendDocumentationContract
            .FindInternalPlanningReferences(ReadRepoFile(relativePath), relativePath, RepoRoot(), allowExternalStandardCitations: true)
            .Should().BeEmpty(because: $"{relativePath} ships to readers who cannot see specs/");
    }

    [Theory]
    [InlineData("DividendHistoryRequest")]
    [InlineData("DividendPaymentDatasetKeyBuilder")]
    [InlineData("Tiingo dividend adapter")]
    [InlineData("docs/dividends.md")]
    public void ChangelogReleaseAdded_AnnouncesEveryShippedDeliverable(string deliverable)
    {
        var leads = DividendDocumentationContract.ReadBoldBulletLeads(
            ReadRepoFile(Changelog), "[2.0.0-rc.20261027.4]", "Added");

        leads.Should().NotBeEmpty(because: "the versioned RC Added block carries this release's entries");
        leads.Any(lead => DividendDocumentationContract.AnnouncesAffirmatively(lead, deliverable))
            .Should().BeTrue(
                because: $"'{deliverable}' shipped, so an Added bullet must lead with it — and lead "
                    + "with it affirmatively, since a bullet denying the deliverable names it just "
                    + "as literally as one announcing it");
    }

    [Fact]
    public void ReadmeTypedRequestTable_MapsDividendHistoryRequestToItsRecordAndSource()
    {
        DividendDocumentationContract.HasCanonicalDividendSource(ReadRepoFile(Readme)).Should().BeTrue(
            because: "the typed-request table must map DividendHistoryRequest to DividendPayment and the canonical provider set");
    }

    [Fact]
    public void Contract_RejectsAReadmeThatNamesAnotherExclusiveDividendSource()
    {
        DividendDocumentationContract.HasCanonicalDividendSource(
            ReadmeClaimingTwelveDataIsTheExclusiveDividendSource).Should().BeFalse();
    }

    [Fact]
    public void ReferenceDataDoc_CitesOnlyRealDividendTypeMembers()
    {
        var real = Enum.GetNames<DividendType>();

        var cited = DividendDocumentationContract.FindCitedDividendTypeMembers(ReadRepoFile(ReferenceDataDoc));

        cited.Should().NotBeEmpty(because: "the reference page enumerates the classification categories");
        cited.Should().OnlyContain(
            member => real.Contains(member, StringComparer.Ordinal),
            because: "a public page citing an API member that does not exist sends the reader to a "
                + $"compile error; the enum's members are [{string.Join(", ", real)}]");
    }

    [Fact]
    public void ReferenceDataDoc_DocumentsEveryDividendTypeMember()
    {
        // The reverse direction of the same lockstep: a member added to the enum without reaching
        // the page it is documented on is how the page came to deny that Unclassified exists.
        var page = ReadRepoFile(ReferenceDataDoc);

        Enum.GetNames<DividendType>().Should().OnlyContain(
            member => page.Contains(member, StringComparison.Ordinal),
            because: "every shipped classification category is part of the contract this page states");
    }

    [Fact]
    public void Contract_RejectsADocumentThatInvertsTheDefaultNormalizerClaim()
    {
        var shippedDefault = new DataPipelineOptions().RegisterDefaultNormalizers;

        DividendDocumentationContract.ReadDefaultNormalizersPolarityClaim(DocumentInvertingTheDefault)
            .Should().Be(
                !shippedDefault,
                because: "this is the assertion the shipped page is graded on, so it has to read the "
                    + "claim's polarity rather than notice that the words appear near each other");
    }

    [Fact]
    public void Contract_RejectsAWorkedExampleThatDisagreesWithItsOwnTable()
    {
        var example = DividendDocumentationContract.ParseWorkedExample(ExampleDisagreeingWithItsOwnTable);

        example.Should().NotBeNull();
        DividendDocumentationContract.FindWorkedExampleDefects(example!).Should().NotBeEmpty(
            because: "a stated total that no sum of the listed rows produces must be caught");
    }

    [Fact]
    public void Contract_rejects_a_worked_example_whose_printed_operands_are_unrelated()
    {
        var example = DividendDocumentationContract.ParseWorkedExample(ExampleWithUnrelatedPrintedOperands);

        example.Should().NotBeNull();
        DividendDocumentationContract.FindWorkedExampleDefects(example!).Should().NotBeEmpty(
            because: "the printed numerator and denominator must agree with the stated cash and price");
    }

    [Fact]
    public void Contract_RejectsAWorkedExampleWhoseWindowCloseDiffersFromItsAsOfDate()
    {
        var example = DividendDocumentationContract.ParseWorkedExample(ExampleWhoseWindowClosesAfterItsAsOfDate);

        example.Should().NotBeNull();
        DividendDocumentationContract.FindWorkedExampleDefects(example!).Should().ContainSingle(
            "the printed window close is 2027-06-30 but the as-of date is 2026-06-30");
    }

    [Fact]
    public void Contract_RejectsAChangelogEntryThatDeniesTheDeliverable()
    {
        var leads = DividendDocumentationContract.ReadBoldBulletLeads(
            ChangelogDenyingItsDeliverables, "[Unreleased]", "Added");

        leads.Should().NotBeEmpty();
        leads.Any(lead => DividendDocumentationContract.AnnouncesAffirmatively(lead, "DividendHistoryRequest"))
            .Should().BeFalse(
                because: "'we did not add DividendHistoryRequest' contains the token and must still fail");
    }

    [Fact]
    public void Contract_RejectsInternalPlanningDocumentCitations()
    {
        DividendDocumentationContract
            .FindInternalPlanningReferences(ChangelogDenyingItsDeliverables, Changelog, RepoRoot(), allowExternalStandardCitations: true)
            .Should().HaveCount(2, because: "the specs/ path and the bare §2.5 each count as a private citation");

        // …while a named external standard's own clause numbering stays legitimate.
        DividendDocumentationContract
            .FindInternalPlanningReferences(
                "30/360 Bond Basis (ISDA 2006 §4.16(f))", Changelog, RepoRoot(), allowExternalStandardCitations: true)
            .Should().BeEmpty();
    }

    [Fact]
    public void Contract_RejectsYieldOwnershipAssignedToTheKernel()
    {
        DividendDocumentationContract.ReadConsumerOwnsYieldDerivationClaim(DocumentAssigningYieldToTheKernel)
            .Should().BeFalse();
    }

    [Fact]
    public void Contract_RejectsOverlappingRangeCachePromise()
    {
        DividendDocumentationContract.ReadExactRangeCacheClaim(DocumentPromisingOverlappingRangeReuse)
            .Should().BeFalse();
    }

    [Fact]
    public void Contract_RecognizesFalsePriceRangeCacheCausality()
    {
        DividendDocumentationContract
            .ClaimsPriceRangeControlsDividendCacheReuse(DocumentClaimingPriceRangeControlsDividendCacheReuse)
            .Should().BeTrue();
    }

    [Fact]
    public void Contract_RejectsDefaultHostNormalizationWhenTheFlagIsOff()
    {
        DividendDocumentationContract.ReadDefaultHostNormalizesDividendPaymentsClaim(DocumentClaimingDefaultHostNormalization)
            .Should().BeTrue();
        new DataPipelineOptions().RegisterDefaultNormalizers.Should().BeFalse();
    }

    [Fact]
    public void Contract_RecognizesAnUnconditionalNormalizedOutputPromise()
    {
        DividendDocumentationContract
            .ClaimsKernelAlwaysReturnsNormalizedDividendPayments(DocumentUnconditionallyPromisingNormalizedKernelOutput)
            .Should().BeTrue();
    }

    [Fact]
    public void Contract_RejectsTheStaleV1SharedCacheShipRecipe()
    {
        DividendDocumentationContract.HasV2SafeShipRecipe(StaleV1ShipRecipe).Should().BeFalse();
    }

    [Fact]
    public void Contract_RejectsAnyNonPublicRelativeMarkdownLink()
    {
        DividendDocumentationContract
            .FindInternalPlanningReferences(DocumentLinkingPrivatePlan, DividendsDoc, RepoRoot(), allowExternalStandardCitations: true)
            .Should().ContainSingle(violation => violation.Contains("planning/dividend-cutover.md", StringComparison.Ordinal));
    }

    [Fact]
    public void Contract_RejectsFileAndDirectorySymlinksThatEscapeThePublicDocsRoot()
    {
        var fixtureRoot = Path.Combine(
            DividendDocumentationContract.ResolveExistingPath(Path.GetTempPath()),
            $"dividend-doc-contract-{Guid.NewGuid():N}");
        var docs = Path.Combine(fixtureRoot, "docs");
        var privateDirectory = Path.Combine(fixtureRoot, "private");
        Directory.CreateDirectory(docs);
        Directory.CreateDirectory(privateDirectory);

        try
        {
            var privateFile = Path.Combine(privateDirectory, "plan.md");
            File.WriteAllText(privateFile, "private");
            File.CreateSymbolicLink(Path.Combine(docs, "linked-plan.md"), privateFile);
            Directory.CreateSymbolicLink(Path.Combine(docs, "linked-private"), privateDirectory);

            var markdown = "[file escape](linked-plan.md) [directory escape](linked-private/plan.md)";
            DividendDocumentationContract
                .FindInternalPlanningReferences(markdown, "docs/index.md", fixtureRoot, allowExternalStandardCitations: true)
                .Should().HaveCount(2)
                .And.OnlyContain(violation => violation.Contains("non-public relative link", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    [Fact]
    public void ChangelogTiingoDividendClaim_DescribesOnlyPositiveDivCashValues()
    {
        var changelog = ReadRepoFile(Changelog);

        DividendDocumentationContract.HasPositiveOnlyDividendCashClaim(changelog).Should().BeTrue(
            because: "the release note must preserve the positive-value threshold and unique-ex-date cardinality");
    }

    [Fact]
    public void Contract_RejectsAChangelogThatAlsoPromisesNegativeDividendPayments()
    {
        DividendDocumentationContract.HasPositiveOnlyDividendCashClaim(
            ChangelogContradictingItsPositiveOnlyClaim).Should().BeFalse();
    }

    [Fact]
    public void Contract_RejectsChangelogThatDescribesNegativeDivCashAsAPayment()
    {
        DividendDocumentationContract.HasPositiveOnlyDividendCashClaim(
            ChangelogDescribingNegativeDivCashAsAPayment).Should().BeFalse();
    }

    [Fact]
    public void Contract_RejectsChangelogThatPromisesOnePaymentPerPositiveBar()
    {
        DividendDocumentationContract.HasPositiveOnlyDividendCashClaim(
            ChangelogPromisingOnePaymentPerPositiveBar).Should().BeFalse();
    }

    [Fact]
    public void DividendsDoc_RejectsNonPositiveAsOfPricesInsteadOfFabricatingZeroYield()
    {
        var page = ReadRepoFile(DividendsDoc);

        page.Should().Contain("priceAsOf <= 0m");
        page.Should().Contain("throw new InvalidOperationException");
        page.Should().NotContain("priceAsOf == 0m ? 0m");
        page.Should().Contain("A missing or non-positive as-of price makes the yield unavailable");
    }

    [Fact]
    public void Contract_RejectsANonexistentDividendTypeCitation()
    {
        var cited = DividendDocumentationContract.FindCitedDividendTypeMembers(
            "| Precise match | Return the specific `DividendType` (e.g., `UsQualified`, `Reit`) |");

        cited.Should().Contain("Reit");
        Enum.GetNames<DividendType>().Should().NotContain("Reit");
    }
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Documentation_contracts_have_the_same_semantics_for_both_line_endings(string newline)
    {
        DividendDocumentationContract.ReadDefaultHostNormalizesDividendPaymentsClaim(
            DocumentClaimingDefaultHostNormalization.ReplaceLineEndings(newline)).Should().BeTrue();
        DividendDocumentationContract.ClaimsPriceRangeControlsDividendCacheReuse(
            DocumentClaimingPriceRangeControlsDividendCacheReuse.ReplaceLineEndings(newline)).Should().BeTrue();
    }

    [Fact]
    public void Changelog_section_lookup_cannot_borrow_bullets_from_another_version()
    {
        const string changelog = "## [Unreleased]\n### Fixed\n- **One**\n## [1.0.0]\n### Added\n- **Old**\n";
        DividendDocumentationContract.ReadBoldBulletLeads(changelog, "[Unreleased]", "Added").Should().BeEmpty();
        DividendDocumentationContract.ReadBoldBulletLeads(changelog, "[Unreleased]", "Fixed").Should().Equal("One");
    }

}
