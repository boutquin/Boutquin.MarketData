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

namespace Boutquin.MarketData.Abstractions.Contracts;

/// <summary>
/// Transforms a raw data record into its canonical form, applying unit conversions, date alignment, and format standardization.
/// </summary>
/// <typeparam name="TRaw">The raw record type from the data source.</typeparam>
/// <typeparam name="TCanonical">The canonical record type after normalization.</typeparam>
/// <remarks>
/// Normalization is deterministic: the same raw input always produces the same canonical output.
/// Common transformations include percent-to-decimal rate conversion, date alignment to business
/// calendars, and duplicate-record resolution. Normalizers are stateless and safe to share across threads.
/// </remarks>
public interface INormalizer<in TRaw, out TCanonical>
{
    /// <summary>
    /// Normalizes a raw record into its canonical representation.
    /// </summary>
    /// <param name="raw">The raw record to normalize.</param>
    /// <returns>The normalized canonical record.</returns>
    TCanonical Normalize(TRaw raw);
}
