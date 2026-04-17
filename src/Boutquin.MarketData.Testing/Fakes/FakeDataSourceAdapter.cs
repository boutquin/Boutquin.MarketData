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
using Boutquin.MarketData.Abstractions.Results;

namespace Boutquin.MarketData.Testing.Fakes;

/// <summary>
/// Deterministic test adapter that returns preconfigured data without making external calls.
/// Useful for integration tests, pipeline verification, and fixture-based testing.
/// </summary>
/// <typeparam name="TRequest">The request type this adapter handles.</typeparam>
/// <typeparam name="TRecord">The record type this adapter returns.</typeparam>
/// <remarks>
/// Configure the adapter with a factory function that produces the desired envelope for
/// any request. Use the <see cref="Invocations"/> property to verify that the pipeline
/// called the adapter with expected requests.
/// </remarks>
public sealed class FakeDataSourceAdapter<TRequest, TRecord> : IDataSourceAdapter<TRequest, TRecord>
    where TRequest : IDataRequest
{
    private readonly Func<TRequest, DataEnvelope<IReadOnlyList<TRecord>>> _factory;
    private readonly Func<TRequest, bool>? _canHandlePredicate;
    private readonly List<TRequest> _invocations = new();

    /// <summary>
    /// Initializes a new fake adapter.
    /// </summary>
    /// <param name="providerKey">The provider key returned by <see cref="ProviderKey"/>.</param>
    /// <param name="factory">Factory producing the response envelope for a given request.</param>
    /// <param name="canHandle">Optional predicate overriding the default (always true) behavior of <c>CanHandle</c>.</param>
    public FakeDataSourceAdapter(
        ProviderCode providerKey,
        Func<TRequest, DataEnvelope<IReadOnlyList<TRecord>>> factory,
        Func<TRequest, bool>? canHandle = null)
    {
        ProviderKey = providerKey;
        _factory = factory;
        _canHandlePredicate = canHandle;
    }

    /// <inheritdoc />
    public ProviderCode ProviderKey { get; }

    /// <summary>
    /// All requests passed to <see cref="FetchAsync"/> in chronological order.
    /// </summary>
    public IReadOnlyList<TRequest> Invocations => _invocations;

    /// <inheritdoc />
    public bool CanHandle(TRequest request) =>
        _canHandlePredicate?.Invoke(request) ?? true;

    /// <inheritdoc />
    public Task<DataEnvelope<IReadOnlyList<TRecord>>> FetchAsync(
        TRequest request, CancellationToken cancellationToken = default)
    {
        _invocations.Add(request);
        return Task.FromResult(_factory(request));
    }
}
