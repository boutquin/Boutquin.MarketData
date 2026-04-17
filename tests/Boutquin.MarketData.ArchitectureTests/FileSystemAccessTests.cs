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

/// <summary>
/// Enforces the rule that all file-system I/O in MarketData is mediated by
/// <c>Boutquin.MarketData.Storage</c>, which wraps <c>Boutquin.Storage.Infrastructure</c>
/// for atomic writes, recovery, and checkpointing.
/// </summary>
/// <remarks>
/// Rationale: consolidating direct <c>System.IO</c> usage in one package keeps crash-safety
/// guarantees (temp-file plus rename, fsync-on-append) and test substitutability
/// (<c>InMemoryObjectStore</c>) in a single testable surface. A leak elsewhere — for example
/// a silently swallowed <c>File.WriteAllText</c> from a DI composition root — would
/// re-introduce the partial-write and non-atomic-replace failure modes this architecture is
/// designed to prevent.
///
/// Banned types (concrete file/directory handles):
///   System.IO.File, System.IO.Directory, System.IO.FileInfo, System.IO.DirectoryInfo,
///   System.IO.FileStream, System.IO.StreamReader, System.IO.StreamWriter.
///
/// Intentionally NOT banned: <c>System.IO.Stream</c>, <c>System.IO.MemoryStream</c>,
/// <c>System.IO.BinaryReader</c>, <c>System.IO.BinaryWriter</c>, <c>System.IO.Path</c>.
/// These are used with in-memory buffers or for pure path-string manipulation — banning them
/// would produce false positives on callers that legitimately read byte streams returned
/// from Storage. <c>Path</c> is a policy decision; see the architecture docs for the
/// rationale on keeping it allowed outside Storage.
/// </remarks>
public sealed class FileSystemAccessTests : BaseArchitectureTest
{
    private static readonly string[] s_bannedFileSystemTypes =
    [
        "System.IO.File",
        "System.IO.Directory",
        "System.IO.FileInfo",
        "System.IO.DirectoryInfo",
        "System.IO.FileStream",
        "System.IO.StreamReader",
        "System.IO.StreamWriter",
    ];

    // Every source assembly except Storage must route file I/O through the Storage package.
    // Storage itself is the single legitimate consumer of Boutquin.Storage.Infrastructure's
    // file-backed primitives and may decode byte streams with StreamReader/Writer.
    public static IEnumerable<object[]> NonStorageAssemblies()
    {
        foreach (var asm in AllSourceAssemblies)
        {
            if (asm == StorageAssembly)
            {
                continue;
            }

            yield return new object[] { asm.GetName().Name! };
        }
    }

    [Theory]
    [MemberData(nameof(NonStorageAssemblies))]
    public void NonStorageAssemblies_ShouldNotTouchConcreteFileSystemTypes(string assemblyName)
    {
        var assembly = AllSourceAssemblies.First(a => a.GetName().Name == assemblyName);

        var result = Types
            .InAssembly(assembly)
            .That()
            // Coverlet injects a tracker class that legitimately uses System.IO.File for hit
            // persistence in CI. Exclude it so the ban stays enforceable.
            .DoNotResideInNamespace("Coverlet.Core.Instrumentation")
            .Should()
            .NotHaveDependencyOnAny(s_bannedFileSystemTypes)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: $"{assemblyName} must route file-system access through " +
                     $"Boutquin.MarketData.Storage (banned types: {string.Join(", ", s_bannedFileSystemTypes)}) " +
                     $"[offenders: {GetFailingTypes(result)}]");
    }
}
