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

namespace Boutquin.MarketData.Abstractions.Primitives;

/// <summary>
/// An ordered sequence of dated observations, forming a univariate time series.
/// </summary>
/// <typeparam name="T">The observation value type.</typeparam>
/// <remarks>
/// Points are expected to be sorted by <see cref="TimeSeriesPoint{T}.Date"/> ascending.
/// No alignment or forward-fill is applied — consumers (builders, recipes) handle
/// panel alignment at the composition layer. This type is intentionally simple:
/// it holds data, not computation.
/// </remarks>
public sealed class TimeSeries<T>
{
    /// <summary>
    /// Initializes a time series from an ordered collection of points.
    /// </summary>
    /// <param name="points">The dated observations, expected in ascending date order.</param>
    public TimeSeries(IReadOnlyList<TimeSeriesPoint<T>> points)
    {
        Points = points;
    }

    /// <summary>
    /// The dated observations in this series, in ascending date order.
    /// </summary>
    public IReadOnlyList<TimeSeriesPoint<T>> Points { get; }

    /// <summary>
    /// The number of observations in this series.
    /// </summary>
    public int Count => Points.Count;

    /// <summary>
    /// Returns <see langword="true"/> if the series contains no observations.
    /// </summary>
    public bool IsEmpty => Points.Count == 0;
}
