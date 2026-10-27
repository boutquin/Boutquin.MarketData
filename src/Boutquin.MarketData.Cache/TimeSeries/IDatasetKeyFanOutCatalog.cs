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
/// Enumerates the closed
/// <see cref="IDatasetKeyFanOut{TRequest, TDatasetKey, TFetched, TStored}"/> interface types
/// registered in the container. The time-series pipeline consults this to discover the fetched-row
/// type <c>TFetched</c> for a given <c>(TRequest, TDatasetKey, TStored)</c> triple — the container
/// cannot resolve an open generic, so the kernel records each registered fan-out here at composition
/// time (mirroring <see cref="IDatasetKeyBuilderCatalog"/>).
/// </summary>
/// <remarks>
/// <para><b>Why the catalogue, not a <c>GetService</c> probe.</b> The keyed-row envelope makes
/// <c>TFetched</c> differ from <c>TStored</c> (<c>OptionChainRow</c> vs <c>OptionQuote</c>). The
/// pipeline's <c>ExecuteAsync&lt;TRequest, TRecord&gt;</c> knows only the stored <c>TRecord</c>, so it
/// cannot form the closed <c>IDatasetKeyFanOut&lt;…, TFetched, …&gt;</c> to probe — it must
/// <em>discover</em> <c>TFetched</c>. This catalogue lists the closed fan-out interface types
/// (generic arguments <c>(TRequest, TDatasetKey, TFetched, TStored)</c>); the executor matches on
/// <c>(TRequest, TDatasetKey, TStored)</c> and reads back <c>TFetched</c>.</para>
/// <para>The body-discriminant fan-outs (<c>YieldCurveQuote</c>, <c>FactorObservation</c>) are the
/// identity case where <c>TFetched == TStored</c>; the option-chain fan-out is the case where they
/// differ. Both are discovered uniformly through this catalogue.</para>
/// </remarks>
public interface IDatasetKeyFanOutCatalog
{
    /// <summary>
    /// The closed <see cref="IDatasetKeyFanOut{TRequest, TDatasetKey, TFetched, TStored}"/> interface
    /// types registered in the container. Each element's generic type arguments are
    /// <c>(TRequest, TDatasetKey, TFetched, TStored)</c>.
    /// </summary>
    IReadOnlyList<Type> RegisteredFanOutInterfaces { get; }
}

/// <summary>
/// Default in-memory <see cref="IDatasetKeyFanOutCatalog"/> built from an explicit list of closed
/// fan-out interface types supplied at composition time.
/// </summary>
public sealed class DatasetKeyFanOutCatalog : IDatasetKeyFanOutCatalog
{
    /// <summary>Creates a catalogue over the supplied closed fan-out interface types.</summary>
    /// <param name="registeredFanOutInterfaces">
    /// The closed <see cref="IDatasetKeyFanOut{TRequest, TDatasetKey, TFetched, TStored}"/> interface
    /// types.
    /// </param>
    public DatasetKeyFanOutCatalog(IReadOnlyList<Type> registeredFanOutInterfaces)
    {
        ArgumentNullException.ThrowIfNull(registeredFanOutInterfaces);
        RegisteredFanOutInterfaces = Array.AsReadOnly(registeredFanOutInterfaces.ToArray());
    }

    /// <inheritdoc />
    public IReadOnlyList<Type> RegisteredFanOutInterfaces { get; }
}
