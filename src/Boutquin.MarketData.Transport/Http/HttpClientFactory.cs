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
/// Factory for creating <see cref="HttpClient"/> instances preconfigured for data provider APIs.
/// </summary>
/// <remarks>
/// <para>
/// Applies a 30-second timeout, a descriptive User-Agent header, and broad Accept headers
/// (JSON, XML, wildcard). The User-Agent identifies the calling product and optionally
/// includes a contact URL, which some official data APIs (e.g., US Treasury, NY Fed) require.
/// </para>
/// <para>
/// For production use with DI, prefer <c>IHttpClientFactory</c> from
/// <c>Microsoft.Extensions.Http</c> and configure named clients. This static factory
/// is suitable for simple scripts, tests, and standalone adapters.
/// </para>
/// </remarks>
public static class HttpClientFactory
{
    private static readonly TimeSpan s_defaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with standard headers for data provider APIs.
    /// </summary>
    /// <param name="productName">Product name included in the User-Agent header (e.g., "Boutquin.MarketData").</param>
    /// <param name="contactUrl">Optional URL included in the User-Agent for API compliance (e.g., repository URL).</param>
    /// <param name="timeout">Request timeout. Defaults to 30 seconds.</param>
    /// <returns>A configured <see cref="HttpClient"/> instance. The caller owns the lifetime.</returns>
    public static HttpClient Create(string productName, string? contactUrl = null, TimeSpan? timeout = null)
    {
        var client = new HttpClient { Timeout = timeout ?? s_defaultTimeout };

        var userAgent = contactUrl is not null
            ? $"{productName}/0.1 (+{contactUrl})"
            : $"{productName}/0.1";

        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/xml, application/xml, */*");

        return client;
    }
}
