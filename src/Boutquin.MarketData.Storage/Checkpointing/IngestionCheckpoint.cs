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
namespace Boutquin.MarketData.Storage.Checkpointing;

/// <summary>
/// Represents the state of an ingestion pipeline at a given checkpoint,
/// enabling resumable fetches after failures or restarts.
/// </summary>
/// <param name="Provider">The data source provider name (e.g., "tiingo", "fred").</param>
/// <param name="Dataset">The dataset being ingested (e.g., "daily-prices", "fx-rates").</param>
/// <param name="SymbolIndex">Zero-based index into the symbol list indicating progress.</param>
/// <param name="LastSuccessUtc">Timestamp of the last successful fetch.</param>
public sealed record IngestionCheckpoint(
    string Provider,
    string Dataset,
    int SymbolIndex,
    DateTimeOffset LastSuccessUtc);
