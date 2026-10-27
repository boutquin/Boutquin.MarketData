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

namespace Boutquin.MarketData.Cache.TimeSeries;

/// <summary>
/// Enumerates the closed <see cref="IDatasetKeyBuilder{TRequest, TDatasetKey}"/> interface types
/// registered in the container. The time-series pipeline consults this to discover the
/// <c>TDatasetKey</c> matching a request's runtime type — the container cannot resolve an open
/// generic (<c>GetService(typeof(IDatasetKeyBuilder&lt;,&gt;))</c> is unsupported), so the kernel
/// records each registered <c>(TRequest, TDatasetKey)</c> pair here at composition time.
/// </summary>
/// <remarks>
/// Registered as a DI singleton by <c>AddMarketDataKernel</c> alongside the builders. A request
/// type absent from this catalogue is not time-series-backed, and <c>DataPipeline</c> falls back to
/// the legacy L2 path for it even when <c>UseTimeSeriesStore</c> is on.
/// </remarks>
public interface IDatasetKeyBuilderCatalog
{
    /// <summary>
    /// The closed <see cref="IDatasetKeyBuilder{TRequest, TDatasetKey}"/> interface types
    /// registered in the container (e.g. <c>IDatasetKeyBuilder&lt;PriceHistoryRequest,
    /// PriceBarDatasetKey&gt;</c>). Each element's generic type arguments are
    /// <c>(TRequest, TDatasetKey)</c>.
    /// </summary>
    IReadOnlyList<Type> RegisteredBuilderInterfaces { get; }
}

/// <summary>
/// Default in-memory <see cref="IDatasetKeyBuilderCatalog"/> built from an explicit list of closed
/// builder interface types supplied at composition time.
/// </summary>
public sealed class DatasetKeyBuilderCatalog : IDatasetKeyBuilderCatalog
{
    /// <summary>Creates a catalogue over the supplied closed builder interface types.</summary>
    /// <param name="registeredBuilderInterfaces">
    /// The closed <see cref="IDatasetKeyBuilder{TRequest, TDatasetKey}"/> interface types.
    /// </param>
    public DatasetKeyBuilderCatalog(IReadOnlyList<Type> registeredBuilderInterfaces)
    {
        ArgumentNullException.ThrowIfNull(registeredBuilderInterfaces);
        RegisteredBuilderInterfaces = Array.AsReadOnly(registeredBuilderInterfaces.ToArray());
    }

    /// <inheritdoc />
    public IReadOnlyList<Type> RegisteredBuilderInterfaces { get; }
}
