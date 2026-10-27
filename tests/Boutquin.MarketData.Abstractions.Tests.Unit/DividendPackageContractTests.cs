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

using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit;

/// <summary>Verifies dividend-guide links on the consumer artifacts inside the packed package.</summary>
public sealed class DividendPackageContractTests
{
    private const int PackTimeoutMilliseconds = 120_000;
    private const string CanonicalDividendGuide =
        "https://github.com/boutquin/Boutquin.MarketData/blob/main/docs/dividends.md";

    [Theory]
    [InlineData("https://github.com/boutquin/Boutquin.MarketData/blob/main/docs/dividends.md", true)]
    [InlineData("https://example.invalid/docs/dividends.md", false)]
    [InlineData("http://github.com/boutquin/Boutquin.MarketData/blob/main/docs/dividends.md", false)]
    [InlineData("docs/dividends.md", false)]
    public void DividendGuideLinkPredicate_AcceptsOnlyAbsoluteHttpsWithoutAnArchiveEntry(
        string target,
        bool expected)
    {
        using var stream = new MemoryStream();
        using (var writer = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            writer.CreateEntry("unrelated.txt");
        }

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        LinkResolves(target, "README.md", archive).Should().Be(expected);
    }

    [Fact]
    public void DividendGuideLinkPredicate_AcceptsAResolvableArchiveEntry()
    {
        using var stream = new MemoryStream();
        using (var writer = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            writer.CreateEntry("docs/dividends.md");
        }

        stream.Position = 0;
        using var reader = new ZipArchive(stream, ZipArchiveMode.Read);

        LinkResolves("docs/dividends.md", "README.md", reader).Should().BeTrue();
        LinkResolves("../../docs/dividends.md", "lib/net10.0/package.xml", reader).Should().BeTrue();
    }

    [Fact]
    public async Task PackedReadmeAndXml_DividendGuideLinksAreAbsoluteOrResolveInsideTheArchive()
    {
        var repoRoot = RepoRoot();
        var probeRoot = Environment.GetEnvironmentVariable("CODE_ARENA_PACKAGE_PROBE_ROOT")
            ?? Path.Combine(Path.GetTempPath(), "Boutquin.MarketData.Tests", nameof(DividendPackageContractTests));
        // The configured location is a parent, never a cleanup target. A caller's seed cannot own an existing directory.
        var scratch = Path.Combine(Path.GetFullPath(probeRoot), Guid.NewGuid().ToString("N"));
        Path.Exists(scratch).Should().BeFalse(because: "package cleanup requires a fresh owned directory");
        var artifacts = Path.Combine(scratch, "artifacts");
        Directory.CreateDirectory(artifacts);

        try
        {
            var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            var project = Path.Combine(
                repoRoot,
                "src/Boutquin.MarketData.Abstractions/Boutquin.MarketData.Abstractions.csproj");
            var startInfo = new ProcessStartInfo(dotnet)
            {
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("pack");
            startInfo.ArgumentList.Add(project);
            startInfo.ArgumentList.Add("--configuration");
            startInfo.ArgumentList.Add("Release");
            startInfo.ArgumentList.Add("--artifacts-path");
            startInfo.ArgumentList.Add(artifacts);
            startInfo.ArgumentList.Add("--disable-build-servers");
            startInfo.ArgumentList.Add("-nodeReuse:false");
            startInfo.ArgumentList.Add("-p:UseSharedCompilation=false");
            startInfo.ArgumentList.Add("-p:NuGetAudit=false");

            using var process = Process.Start(startInfo);
            process.Should().NotBeNull(because: "the package contract must exercise the actual dotnet pack output");
            var standardOutputTask = process!.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();
            var timedOut = false;
            using var timeout = new CancellationTokenSource(PackTimeoutMilliseconds);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                timedOut = true;
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            timedOut.Should().BeFalse(
                because: $"the isolated Abstractions pack must finish within {PackTimeoutMilliseconds} ms.\n"
                    + $"{standardOutput}\n{standardError}");
            process.ExitCode.Should().Be(
                0,
                because: $"the isolated Abstractions pack must succeed before links can be checked.\n{standardOutput}\n{standardError}");

            var package = Directory
                .EnumerateFiles(
                    artifacts,
                    "Boutquin.MarketData.Abstractions.*.nupkg",
                    SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
                .Should().ContainSingle(because: "the isolated pack produces one Abstractions consumer package")
                .Subject;
            using var archive = ZipFile.OpenRead(package);

            const string readmeEntry = "README.md";
            const string xmlEntry = "lib/net10.0/Boutquin.MarketData.Abstractions.xml";
            var readme = ReadEntry(archive, readmeEntry);
            var xml = ReadEntry(archive, xmlEntry);
            var readmeTarget = Regex.Match(
                readme,
                @"\[[^\]]+\]\((?<target>[^)\s]*dividends\.md)\)",
                RegexOptions.CultureInvariant);
            var xmlTarget = Regex.Match(
                xml,
                "href=\\\"(?<target>[^\\\"]*dividends\\.md)\\\"",
                RegexOptions.CultureInvariant);

            readmeTarget.Success.Should().BeTrue(because: "the packaged README must name the dividend guide");
            xmlTarget.Success.Should().BeTrue(because: "the packaged request XML must name the dividend guide");

            var unresolvedDividendGuideLinks = new List<string>();
            if (!LinkResolves(readmeTarget.Groups["target"].Value, readmeEntry, archive))
            {
                unresolvedDividendGuideLinks.Add($"{readmeEntry} -> {readmeTarget.Groups["target"].Value}");
            }

            if (!LinkResolves(xmlTarget.Groups["target"].Value, xmlEntry, archive))
            {
                unresolvedDividendGuideLinks.Add($"{xmlEntry} -> {xmlTarget.Groups["target"].Value}");
            }

            string.Join(Environment.NewLine, unresolvedDividendGuideLinks).Should().BeEmpty(
                because: "each packaged dividend-guide link must be the canonical public guide or resolve to an archive entry");
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("existing")]
    [InlineData(".")]
    [Trait("Tier", "Integration")]
    public async Task PackedReadmeAndXml_PreservesPreexistingProbeContent(string seed)
    {
        var fixture = Directory.CreateTempSubdirectory("DividendPackageCleanup-");
        var selectedDirectory = Directory.CreateDirectory(Path.Combine(fixture.FullName, seed));
        var sentinel = Path.Combine(selectedDirectory.FullName, "sentinel.txt");
        await File.WriteAllTextAsync(sentinel, "preexisting content");
        var previousRoot = Environment.GetEnvironmentVariable("CODE_ARENA_PACKAGE_PROBE_ROOT");
        var previousSeed = Environment.GetEnvironmentVariable("CODE_ARENA_PACKAGE_PROBE_SEED");

        try
        {
            Environment.SetEnvironmentVariable("CODE_ARENA_PACKAGE_PROBE_ROOT", fixture.FullName);
            Environment.SetEnvironmentVariable("CODE_ARENA_PACKAGE_PROBE_SEED", seed);

            await PackedReadmeAndXml_DividendGuideLinksAreAbsoluteOrResolveInsideTheArchive();

            File.Exists(sentinel).Should().BeTrue(because: "package cleanup must preserve preexisting caller content");
            (await File.ReadAllTextAsync(sentinel)).Should().Be("preexisting content");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODE_ARENA_PACKAGE_PROBE_ROOT", previousRoot);
            Environment.SetEnvironmentVariable("CODE_ARENA_PACKAGE_PROBE_SEED", previousSeed);
            if (fixture.Exists)
            {
                fixture.Delete(recursive: true);
            }
        }
    }

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

    private static string ReadEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        entry.Should().NotBeNull(because: $"the consumer package must contain {path}");
        using var reader = new StreamReader(entry!.Open());
        return reader.ReadToEnd();
    }

    private static bool LinkResolves(string target, string originEntry, ZipArchive archive)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return string.Equals(uri.AbsoluteUri, CanonicalDividendGuide, StringComparison.Ordinal);
        }

        var resolvedSegments = originEntry.Split('/').SkipLast(1).ToList();
        foreach (var segment in target.Replace('\\', '/').Split('/'))
        {
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (resolvedSegments.Count == 0)
                {
                    return false;
                }

                resolvedSegments.RemoveAt(resolvedSegments.Count - 1);
            }
            else
            {
                resolvedSegments.Add(segment);
            }
        }

        return archive.GetEntry(string.Join('/', resolvedSegments)) is not null;
    }
}
