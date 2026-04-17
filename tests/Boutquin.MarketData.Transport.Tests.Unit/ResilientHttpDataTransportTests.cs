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

using Boutquin.MarketData.Testing.Fakes;
using Boutquin.MarketData.Transport.Http;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Transport.Tests.Unit;

public sealed class ResilientHttpDataTransportTests
{
    private static readonly Uri s_testUri = new("https://example.com/api/data");

    private static TransportOptions NoResilienceOptions() => new()
    {
        EnableRetry = false,
        EnableRateLimiting = false,
    };

    [Fact]
    public async Task GetAsync_success_returns_stream()
    {
        using var handler = new FakeHttpMessageHandler("hello");
        using var client = new HttpClient(handler);
        using var transport = new ResilientHttpDataTransport(client, NoResilienceOptions());

        using var stream = await transport.GetAsync(s_testUri);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        content.Should().Be("hello");
    }

    [Fact]
    public async Task GetAsync_passes_custom_headers()
    {
        using var handler = new FakeHttpMessageHandler("ok");
        using var client = new HttpClient(handler);
        using var transport = new ResilientHttpDataTransport(client, NoResilienceOptions());

        var headers = new Dictionary<string, string>
        {
            ["X-Custom"] = "test-value",
            ["Authorization"] = "Bearer token123",
        };

        await transport.GetAsync(s_testUri, headers);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.GetValues("X-Custom").Should().Contain("test-value");
        handler.LastRequest.Headers.GetValues("Authorization").Should().Contain("Bearer token123");
    }

    [Fact]
    public async Task GetAsync_404_throws_HttpRequestException()
    {
        using var handler = new FakeHttpMessageHandler("not found", HttpStatusCode.NotFound);
        using var client = new HttpClient(handler);
        using var transport = new ResilientHttpDataTransport(client, NoResilienceOptions());

        var act = () => transport.GetAsync(s_testUri);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetAsync_cancellation_throws_OperationCanceledException()
    {
        using var handler = new FakeHttpMessageHandler("ok");
        using var client = new HttpClient(handler);
        using var transport = new ResilientHttpDataTransport(client, NoResilienceOptions());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => transport.GetAsync(s_testUri, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Dispose_disposes_owned_client()
    {
        var transport = new ResilientHttpDataTransport(options: NoResilienceOptions());
        transport.Dispose();

        var act = () => transport.GetAsync(s_testUri);

        act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_does_not_dispose_injected_client()
    {
        using var handler = new FakeHttpMessageHandler("ok");
        using var client = new HttpClient(handler);

        var transport = new ResilientHttpDataTransport(client, NoResilienceOptions(), ownsClient: false);
        transport.Dispose();

        // Client should still be usable after transport disposal.
        var response = await client.GetAsync(s_testUri);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAsync_500_retries_and_succeeds()
    {
        using var handler = new SequentialResponseHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("error", Encoding.UTF8),
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok", Encoding.UTF8),
            });

        using var client = new HttpClient(handler);
        var opts = new TransportOptions
        {
            EnableRetry = true,
            MaxRetryAttempts = 3,
            BaseRetryDelay = TimeSpan.FromMilliseconds(1),
            EnableRateLimiting = false,
        };

        using var transport = new ResilientHttpDataTransport(client, opts);
        using var stream = await transport.GetAsync(s_testUri);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        content.Should().Be("ok");
    }

    [Fact]
    public async Task GetAsync_all_retries_exhausted_throws()
    {
        using var handler = new SequentialResponseHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("fail1", Encoding.UTF8),
            },
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("fail2", Encoding.UTF8),
            },
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("fail3", Encoding.UTF8),
            },
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("fail4", Encoding.UTF8),
            });

        using var client = new HttpClient(handler);
        var opts = new TransportOptions
        {
            EnableRetry = true,
            MaxRetryAttempts = 2,
            BaseRetryDelay = TimeSpan.FromMilliseconds(1),
            EnableRateLimiting = false,
        };

        using var transport = new ResilientHttpDataTransport(client, opts);

        var act = () => transport.GetAsync(s_testUri);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    /// <summary>
    /// Returns different <see cref="HttpResponseMessage"/> instances on each call,
    /// enabling retry-scenario tests where FakeHttpMessageHandler's single-response
    /// model is insufficient.
    /// </summary>
    private sealed class SequentialResponseHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequentialResponseHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count > 0)
            {
                return Task.FromResult(_responses.Dequeue());
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("exhausted", Encoding.UTF8),
            });
        }
    }
}
