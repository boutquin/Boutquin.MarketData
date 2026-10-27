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
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Catalog.Governance;

namespace Boutquin.MarketData.Catalog.Descriptors;

/// <summary>
/// Immutable metadata describing a data source's identity, governance posture, and capabilities.
/// Applications may use this metadata to define their own source-selection policies.
/// </summary>
/// <param name="SourceId">Stable descriptive identifier (e.g., "nyfed-sofr", "tiingo-equity").</param>
/// <param name="DisplayName">Human-readable label for logging and diagnostics.</param>
/// <param name="Tier">Governance tier available to application-level selection policies.</param>
/// <param name="LicenseFlag">Descriptive licensing posture; the kernel does not enforce it.</param>
/// <param name="PrimaryFrequency">The primary publication cadence of this source.</param>
/// <param name="SupportsHistorical">Whether this source provides historical backfill data.</param>
/// <param name="SupportsEndOfDay">Whether this source provides end-of-day snapshots.</param>
/// <param name="SupportsIntraday">Whether this source provides intraday or real-time data.</param>
/// <param name="SupportsRedistribution">Whether downstream redistribution is permitted under the license.</param>
/// <param name="Notes">Operational caveats, known limitations, or integration status notes.</param>
/// <remarks>
/// <para>
/// Domain-specific classification (currency, instrument family) belongs in the domain repo's
/// adapter, not here. This record is passive metadata. The registry routes adapters using
/// their provider keys and request support; the pipeline does not automatically consume
/// this descriptor's tier, license flag, or capability fields.
/// </para>
/// <para>
/// Applications should keep <see cref="SourceId"/> unique within their descriptor catalogue.
/// Use lowercase-with-hyphens convention (e.g., "bank-of-canada-valet", "fred-series").
/// </para>
/// </remarks>
public sealed record SourceDescriptor(
    string SourceId,
    string DisplayName,
    DataSourceTier Tier,
    DataLicenseFlag LicenseFlag,
    DataFrequency PrimaryFrequency,
    bool SupportsHistorical = true,
    bool SupportsEndOfDay = true,
    bool SupportsIntraday = false,
    bool SupportsRedistribution = false,
    string? Notes = null);
