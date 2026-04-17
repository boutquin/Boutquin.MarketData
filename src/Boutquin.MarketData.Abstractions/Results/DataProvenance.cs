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

using Boutquin.MarketData.Abstractions.Provenance;

namespace Boutquin.MarketData.Abstractions.Results;

/// <summary>
/// Records the origin and retrieval context of fetched data for auditability and reproducibility.
/// </summary>
/// <param name="Provider">The data provider key (e.g., <c>new ProviderCode("tiingo")</c>).</param>
/// <param name="Dataset">The dataset identifier within the provider (e.g., "AAPL", "SOFR", "DGS10").</param>
/// <param name="LicenseFlag">The license classification governing redistribution and audit rules.</param>
/// <param name="RetrievalMode">How the data was obtained: API, cache, snapshot, derived, or fixture.</param>
/// <param name="Freshness">How current the underlying data is at source, independent of retrieval mode.</param>
/// <param name="RetrievedAtUtc">The UTC timestamp when the data was retrieved or served from cache.</param>
/// <param name="SourceUrl">The source URL used for retrieval, or <see langword="null"/> if served from cache.</param>
/// <param name="DataDate">The business date of the actual data served, or <see langword="null"/> when unknown.
/// For EOD sources this is the trading date whose close data was returned — which may differ from
/// the requested as-of date when lookback or closest-date logic is applied.</param>
/// <remarks>
/// Provenance is attached to every <see cref="DataEnvelope{TPayload}"/> and is critical for
/// license compliance (distinguishing free vs. commercial data via <see cref="LicenseType"/>),
/// staleness detection (comparing <see cref="RetrievedAtUtc"/> to current time and
/// <see cref="Freshness"/>), and debugging data quality issues (tracing which adapter and URL
/// produced a given result). <see cref="RetrievalMode"/> and <see cref="Freshness"/> are
/// orthogonal: a cache hit can still be <see cref="FreshnessClass.Live"/> if recently fetched;
/// an API call can return <see cref="FreshnessClass.Stale"/> data if the source has not updated.
/// </remarks>
public sealed record DataProvenance(
    ProviderCode Provider,
    string Dataset,
    LicenseType LicenseFlag,
    RetrievalMode RetrievalMode,
    FreshnessClass Freshness,
    DateTimeOffset RetrievedAtUtc,
    string? SourceUrl,
    DateOnly? DataDate = null);
