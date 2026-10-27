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

namespace Boutquin.MarketData.Orchestration.Pipeline;

internal interface ITimeSeriesSelectedProviderIndex
{
    bool TryGet(Type requestType, string datasetKey, out ProviderCode provider);

    void Record(Type requestType, string datasetKey, ProviderCode provider);
}

internal sealed class InMemoryTimeSeriesSelectedProviderIndex : ITimeSeriesSelectedProviderIndex
{
    private readonly object _gate = new();
    private readonly Dictionary<(Type RequestType, string DatasetKey), Entry> _providers = [];
    private readonly int _capacity;
    private readonly TimeSpan _ttl;
    private readonly IClock _clock;

    internal InMemoryTimeSeriesSelectedProviderIndex(int capacity, TimeSpan ttl, IClock clock)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(clock);

        _capacity = capacity;
        _ttl = ttl;
        _clock = clock;
    }

    public bool TryGet(Type requestType, string datasetKey, out ProviderCode provider)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(datasetKey);

        lock (_gate)
        {
            RemoveExpired();
            var key = (requestType, datasetKey);
            if (_providers.TryGetValue(key, out var entry))
            {
                _providers[key] = entry with { LastAccessUtc = _clock.UtcNow };
                provider = entry.Provider;
                return true;
            }

            provider = default;
            return false;
        }
    }

    public void Record(Type requestType, string datasetKey, ProviderCode provider)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(datasetKey);

        lock (_gate)
        {
            RemoveExpired();
            var now = _clock.UtcNow;
            _providers[(requestType, datasetKey)] = new Entry(provider, now, now + _ttl);
            while (_providers.Count > _capacity)
            {
                var oldest = _providers.MinBy(static pair => pair.Value.LastAccessUtc).Key;
                _providers.Remove(oldest);
            }
        }
    }

    private void RemoveExpired()
    {
        var now = _clock.UtcNow;
        foreach (var key in _providers
            .Where(pair => pair.Value.ExpiresAtUtc <= now)
            .Select(static pair => pair.Key)
            .ToList())
        {
            _providers.Remove(key);
        }
    }

    private sealed record Entry(ProviderCode Provider, DateTimeOffset LastAccessUtc, DateTimeOffset ExpiresAtUtc);
}
