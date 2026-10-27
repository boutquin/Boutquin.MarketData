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

namespace Boutquin.MarketData.Normalization.Normalizers;

/// <summary>
/// Applies the normalizer family's input policy at the public normalization boundary.
/// </summary>
internal static class NormalizerInputGuard
{
    /// <summary>
    /// Rejects a null reference record with the shared <c>raw</c> parameter contract.
    /// Value-type records pass through without allocation.
    /// </summary>
    internal static void ThrowIfNull<T>(T raw)
    {
        if (raw is null)
        {
            throw new ArgumentNullException(nameof(raw));
        }
    }

    /// <summary>
    /// Rejects a null required string before a normalizer calls string APIs or canonical-name
    /// helpers, preserving the member name in the diagnostic.
    /// </summary>
    internal static void ThrowIfRequiredStringIsNull(
        string? value,
        string memberName,
        string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentException(
                $"Required string member '{memberName}' cannot be null.",
                parameterName);
        }
    }
}
