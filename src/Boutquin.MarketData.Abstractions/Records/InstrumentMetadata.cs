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

using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// Reference metadata for a financial instrument (ETF, equity, or similar security).
/// Provides identifying and descriptive attributes — not time-series data.
/// </summary>
/// <param name="Symbol">The exchange ticker symbol (e.g., "VTI", "XIC"), strongly typed.</param>
/// <param name="Name">The instrument's full display name.</param>
/// <param name="Exchange">The primary exchange or listing venue (ISO 10383 MIC).</param>
/// <param name="AssetClass">The asset-class classification for cross-asset pipelines.</param>
/// <param name="SymbolStandard">
/// The identifier standard the <paramref name="Symbol"/> is expressed in.
/// <see langword="null"/> when unknown or not applicable (e.g., generic tickers).
/// </param>
/// <param name="InceptionDate">The date the instrument began trading, or <see langword="null"/>
/// if the data source does not provide this field.</param>
/// <param name="Description">A free-text description of the instrument, or <see langword="null"/>
/// if unavailable.</param>
public sealed record InstrumentMetadata(
    Symbol Symbol,
    string Name,
    ExchangeCode Exchange,
    AssetClassCode AssetClass,
    SecuritySymbolStandard? SymbolStandard,
    DateOnly? InceptionDate,
    string? Description);
