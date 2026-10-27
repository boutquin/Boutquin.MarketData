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

namespace Boutquin.MarketData.Abstractions.Enums;

/// <summary>
/// Governs how the pipeline reacts when a columnar row group fails its per-row-group CRC32C
/// check on read and is reported quarantined by the Boutquin.Storage engine.
/// </summary>
/// <remarks>
/// <para>A quarantined row group is one whose on-disk bytes did not authenticate; the engine
/// surfaces it rather than feeding corrupt values downstream. This enum chooses the
/// consumer-facing reaction. The default <see cref="TreatAsGap"/> keeps the cache tier
/// self-healing: a corrupt row group is treated as missing coverage, so the next request
/// re-fetches the affected window via gap fill rather than surfacing an error to analytics.</para>
/// </remarks>
public enum QuarantineBehavior
{
    /// <summary>
    /// Fail the whole query with a typed exception the instant any row group is quarantined.
    /// Strictest policy; appropriate for regulatory all-or-nothing reads.
    /// </summary>
    FailQuery,

    /// <summary>
    /// Skip the quarantined row group and return the remaining authenticated rows, recording a
    /// data-quality incident. Coverage is unaffected; the gap is silent to callers.
    /// </summary>
    SkipRowGroup,

    /// <summary>
    /// Treat the quarantined row group as a coverage gap so a subsequent request re-fetches the
    /// affected window via gap fill. The self-healing default.
    /// </summary>
    TreatAsGap,
}
