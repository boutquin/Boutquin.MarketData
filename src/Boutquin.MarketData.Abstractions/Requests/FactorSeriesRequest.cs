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

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>
/// Requests academic factor return series (e.g., Fama-French 3-factor, 5-factor, momentum).
/// </summary>
/// <param name="DatasetName">The factor dataset name (e.g., <c>new FactorDatasetId("F-F_Research_Data_Factors_daily")</c>).</param>
/// <param name="Range">The inclusive date range for the request.</param>
/// <param name="Frequency">The observation frequency. Defaults to <see cref="DataFrequency.Daily"/>.</param>
public sealed record FactorSeriesRequest(
    FactorDatasetId DatasetName,
    DateRange Range,
    DataFrequency Frequency = DataFrequency.Daily) : IDataRequest
{
    /// <inheritdoc />
    public string DatasetKey => DatasetName.Value;
}
