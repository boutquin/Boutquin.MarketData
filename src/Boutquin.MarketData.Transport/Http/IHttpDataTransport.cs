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

namespace Boutquin.MarketData.Transport.Http;

/// <summary>
/// Abstraction over HTTP GET operations for fetching raw data streams from remote endpoints.
/// </summary>
/// <remarks>
/// The transport layer is intentionally separated from domain logic. Adapters depend on
/// this interface rather than <see cref="HttpClient"/> directly, enabling resilience policies
/// (retry, backoff, circuit breaking) to be applied uniformly without duplicating logic
/// across adapters. Implementations must return a seekable <see cref="Stream"/> that callers
/// can read independently of the underlying HTTP connection.
/// </remarks>
public interface IHttpDataTransport
{
    /// <summary>
    /// Sends an HTTP GET request to the specified URI and returns the response body as a stream.
    /// </summary>
    /// <param name="uri">The target URI to fetch data from.</param>
    /// <param name="headers">Optional HTTP headers to include in the request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A <see cref="Stream"/> containing the response body.</returns>
    Task<Stream> GetAsync(Uri uri, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default);
}
