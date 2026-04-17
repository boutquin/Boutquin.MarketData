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
using Boutquin.MarketData.Abstractions.Provenance;

namespace Boutquin.MarketData.Catalog.Registry;

/// <summary>
/// In-memory index of data source adapters, providing lookup by provider key and predicate-based filtering.
/// </summary>
/// <typeparam name="TRequest">The request type handled by the adapters.</typeparam>
/// <typeparam name="TRecord">The record type produced by the adapters.</typeparam>
/// <remarks>
/// <para>
/// Adapters are indexed by <see cref="IDataSourceAdapter{TRequest, TRecord}.ProviderKey"/>.
/// <see cref="ProviderCode"/> already normalises to lower-case, so equality is
/// case-insensitive by construction. Duplicate provider keys cause an
/// <see cref="ArgumentException"/> at construction time.
/// </para>
/// <para>
/// The registry is immutable after construction. All query methods return read-only collections
/// that are safe to enumerate concurrently.
/// </para>
/// </remarks>
public sealed class SourceRegistry<TRequest, TRecord>
    where TRequest : IDataRequest
{
    private readonly IReadOnlyDictionary<ProviderCode, IDataSourceAdapter<TRequest, TRecord>> _adaptersByKey;
    private readonly IReadOnlyList<IDataSourceAdapter<TRequest, TRecord>> _allAdapters;

    /// <summary>
    /// Initializes a new registry from the provided adapters.
    /// </summary>
    /// <param name="adapters">The adapters to register. Provider keys must be unique (case-insensitive via <see cref="ProviderCode"/> normalisation).</param>
    /// <exception cref="ArgumentException">Thrown when two adapters share the same provider key.</exception>
    public SourceRegistry(IEnumerable<IDataSourceAdapter<TRequest, TRecord>> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        var list = adapters.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] is null)
            {
                throw new ArgumentException(
                    $"Adapter at index {i} is null.",
                    nameof(adapters));
            }
        }

        _allAdapters = list.AsReadOnly();

        try
        {
            _adaptersByKey = list.ToDictionary(a => a.ProviderKey, a => a);
        }
        catch (ArgumentException ex)
        {
            // ToDictionary throws with just the duplicate key. Re-raise with the full key set
            // so operators diagnosing a duplicate can see all registered providers at once.
            var keys = string.Join(", ", list.Select(a => a.ProviderKey));
            throw new ArgumentException(
                $"Duplicate provider key in adapter registration. Registered keys: [{keys}].",
                nameof(adapters),
                ex);
        }
    }

    /// <summary>
    /// All registered adapters.
    /// </summary>
    public IReadOnlyList<IDataSourceAdapter<TRequest, TRecord>> All => _allAdapters;

    /// <summary>
    /// Looks up an adapter by its provider key.
    /// </summary>
    /// <param name="providerKey">The provider key (case-insensitive via <see cref="ProviderCode"/> normalisation).</param>
    /// <param name="adapter">The matching adapter, or <see langword="null"/> if not found.</param>
    /// <returns><see langword="true"/> if found; otherwise <see langword="false"/>.</returns>
    public bool TryGetByKey(ProviderCode providerKey, out IDataSourceAdapter<TRequest, TRecord>? adapter) =>
        _adaptersByKey.TryGetValue(providerKey, out adapter);

    /// <summary>
    /// Returns all adapters that can handle the specified request.
    /// </summary>
    /// <param name="request">The request to match against each adapter's <c>CanHandle</c> method.</param>
    /// <returns>Adapters that report they can handle the request, in registration order.</returns>
    public IReadOnlyList<IDataSourceAdapter<TRequest, TRecord>> GetHandlers(TRequest request) =>
        _allAdapters.Where(a => a.CanHandle(request)).ToList().AsReadOnly();

    /// <summary>
    /// Returns the adapter for the specified provider key, or throws if not found.
    /// </summary>
    /// <param name="providerKey">The provider key (case-insensitive via <see cref="ProviderCode"/> normalisation).</param>
    /// <returns>The matching adapter.</returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no adapter is registered with the specified <paramref name="providerKey"/>.
    /// </exception>
    public IDataSourceAdapter<TRequest, TRecord> GetRequired(ProviderCode providerKey) =>
        _adaptersByKey.TryGetValue(providerKey, out var adapter)
            ? adapter
            : throw new KeyNotFoundException(
                $"No adapter registered for provider key '{providerKey}'. " +
                $"Registered keys: [{string.Join(", ", _adaptersByKey.Keys)}].");

    /// <summary>
    /// Returns all adapters matching a custom predicate.
    /// </summary>
    /// <param name="predicate">The filter to apply.</param>
    /// <returns>Matching adapters in registration order.</returns>
    public IReadOnlyList<IDataSourceAdapter<TRequest, TRecord>> GetBy(
        Func<IDataSourceAdapter<TRequest, TRecord>, bool> predicate) =>
        _allAdapters.Where(predicate).ToList().AsReadOnly();
}
