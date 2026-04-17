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
using System.Net;
using System.Text;

namespace Boutquin.MarketData.Testing.Fakes;

/// <summary>
/// Test double for <see cref="HttpMessageHandler"/> that returns preconfigured responses
/// without making real HTTP calls. Supports per-URL response mapping with substring fallback.
/// </summary>
/// <remarks>
/// <para>
/// Use this handler to test adapters that depend on <see cref="HttpClient"/> without hitting
/// real endpoints. Pass it to <c>new HttpClient(handler)</c> or to
/// <c>ResilientHttpDataTransport</c>.
/// </para>
/// <para>
/// Response matching order: exact URL match first, then substring containment, then the
/// default response. This avoids test fragility when URL query parameters vary.
/// </para>
/// </remarks>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    // Responses are stored as factories rather than live HttpResponseMessage instances so every
    // SendAsync returns a fresh response. HttpResponseMessage.Content streams are consumed on
    // first read — sharing a single instance across requests caused test flake when the same
    // URL was hit twice and the second caller got an empty body.
    private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<HttpResponseMessage> _defaultResponseFactory;
    private readonly List<HttpRequestMessage> _allRequests = [];

    /// <summary>
    /// Initializes a new handler with a default response body and status code.
    /// </summary>
    /// <param name="defaultContent">Default response body returned when no URL-specific mapping matches.</param>
    /// <param name="defaultStatusCode">Default HTTP status code. Defaults to <see cref="HttpStatusCode.OK"/>.</param>
    public FakeHttpMessageHandler(string defaultContent = "", HttpStatusCode defaultStatusCode = HttpStatusCode.OK)
    {
        _defaultResponseFactory = () => new HttpResponseMessage(defaultStatusCode)
        {
            Content = new StringContent(defaultContent, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// All requests sent through this handler, in chronological order.
    /// </summary>
    public IReadOnlyList<HttpRequestMessage> AllRequests => _allRequests;

    /// <summary>
    /// The most recent request sent through this handler, or <see langword="null"/> if none.
    /// </summary>
    public HttpRequestMessage? LastRequest => _allRequests.Count > 0 ? _allRequests[^1] : null;

    /// <summary>
    /// Registers a response for a specific URL (exact or substring match).
    /// </summary>
    /// <param name="urlKey">The URL or URL fragment to match against.</param>
    /// <param name="content">The response body.</param>
    /// <param name="statusCode">The HTTP status code. Defaults to <see cref="HttpStatusCode.OK"/>.</param>
    /// <returns>This handler, for fluent chaining.</returns>
    public FakeHttpMessageHandler RespondTo(string urlKey, string content, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses[urlKey] = () => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };
        return this;
    }

    /// <summary>
    /// Registers a binary response for a specific URL (e.g., ZIP files).
    /// </summary>
    /// <param name="urlKey">The URL or URL fragment to match against.</param>
    /// <param name="bytes">The raw response bytes.</param>
    /// <param name="mediaType">The content type. Defaults to "application/octet-stream".</param>
    /// <param name="statusCode">The HTTP status code. Defaults to <see cref="HttpStatusCode.OK"/>.</param>
    /// <returns>This handler, for fluent chaining.</returns>
    public FakeHttpMessageHandler RespondToWithBytes(
        string urlKey, byte[] bytes, string mediaType = "application/octet-stream",
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responses[urlKey] = () =>
        {
            // Copy so callers that mutate `bytes` after registration do not affect later responses.
            var copy = new byte[bytes.Length];
            Array.Copy(bytes, copy, bytes.Length);
            var resp = new HttpResponseMessage(statusCode)
            {
                Content = new ByteArrayContent(copy),
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            return resp;
        };
        return this;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _allRequests.Add(request);
        var url = request.RequestUri?.ToString() ?? string.Empty;

        // Exact match first.
        if (_responses.TryGetValue(url, out var exactFactory))
        {
            return Task.FromResult(exactFactory());
        }

        // Substring match fallback.
        foreach (var (key, factory) in _responses)
        {
            if (url.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(factory());
            }
        }

        return Task.FromResult(_defaultResponseFactory());
    }
}
