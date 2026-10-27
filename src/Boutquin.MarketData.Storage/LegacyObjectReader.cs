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
namespace Boutquin.MarketData.Storage;

// Read-only compatibility for files written before Storage added encoded object filenames.
// Callers supply validated relative keys. Descendant links are never followed; storage roots
// and their directory entries must be controlled by the application, as for the current backend.
internal static class LegacyObjectReader
{
    internal static string? Find(string root, string relativeKey, CancellationToken cancellationToken)
    {
        try
        {
            var current = root;
            foreach (var segment in relativeKey.Split('/'))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = Directory.EnumerateFileSystemEntries(current)
                    .FirstOrDefault(path => string.Equals(Path.GetFileName(path), segment, StringComparison.Ordinal));
                if (entry is null || (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                {
                    return null;
                }

                current = entry;
            }

            return File.Exists(current) ? current : null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
    }

    internal static Stream? Open(string root, string relativeKey, CancellationToken cancellationToken)
    {
        var path = Find(root, relativeKey, cancellationToken);
        if (path is null)
        {
            return null;
        }

        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }
}
