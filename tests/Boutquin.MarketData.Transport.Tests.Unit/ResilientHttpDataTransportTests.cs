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
using System.Net.Sockets;
using System.Text;
using Boutquin.MarketData.Testing.Fakes;
using Boutquin.MarketData.Transport.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Polly.RateLimiting;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_RejectsDisposedTransportWithBorrowedClient(bool asynchronously)
    {
        using var handler = new FakeHttpMessageHandler("ok");
        using var client = new HttpClient(handler);
        using var transport = new ResilientHttpDataTransport(client, NoResilienceOptions());
        if (asynchronously)
        {
            await transport.DisposeAsync().ConfigureAwait(true);
        }
        else
        {
            transport.Dispose();
        }

        var act = () => transport.GetAsync(s_testUri);

        await act.Should().ThrowAsync<ObjectDisposedException>().ConfigureAwait(true);
        handler.LastRequest.Should().BeNull();
        using var response = await client.GetAsync(s_testUri).ConfigureAwait(true);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAsync_BoundsAdvertisedInitialCapacityBeforeNarrowing()
    {
        using var content = new ByteArrayContent([42]);
        content.Headers.ContentLength = (long)int.MaxValue + 1;
        using var handler = new SequentialResponseHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using var client = new HttpClient(handler);
        var options = NoResilienceOptions();
        options.MaxResponseBytes = (long)int.MaxValue + 1;
        using var transport = new ResilientHttpDataTransport(client, options);
        Stream? result = null;
        var act = async () => result = await transport.GetAsync(s_testUri).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
        using var stream = result!;
        stream.Length.Should().Be(1);
        stream.ReadByte().Should().Be(42);
        ((MemoryStream)stream).Capacity.Should().BeLessThanOrEqualTo(81920);
    }

    [Fact]
    public void Constructor_AcceptsLongResponseCapWithDefaultClient()
    {
        var options = NoResilienceOptions();
        options.MaxResponseBytes = (long)int.MaxValue + 1;
        Action act = () =>
        {
            using var transport = new ResilientHttpDataTransport(options: options);
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_RejectsNegativeResponseCap()
    {
        var options = NoResilienceOptions();
        options.MaxResponseBytes = -1;
        var act = () => new ResilientHttpDataTransport(options: options);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("http://[::ffff:10.2.3.4]/data")]
    [InlineData("http://[::ffff:169.254.169.254]/data")]
    public async Task GetAsync_RejectsMappedPrivateAddressBeforeDispatch(string address)
    {
        using var handler = new FakeHttpMessageHandler("ok");
        using var client = new HttpClient(handler);
        using var transport = new ResilientHttpDataTransport(client, NoResilienceOptions());
        var act = () => transport.GetAsync(new Uri(address));

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
        handler.LastRequest.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("?token=fixturetoken")]
    [InlineData("?format=json")]
    public async Task GetAsync_RedactsUserInfoInEveryLog(string query)
    {
        using var handler = new FakeHttpMessageHandler("ok");
        using var client = new HttpClient(handler);
        var logger = new RecordingLogger();
        using var transport = new ResilientHttpDataTransport(client, NoResilienceOptions(), logger);

        using var stream = await transport.GetAsync(new Uri("https://fixtureuser:fixturepassword@example.com/data" + query)).ConfigureAwait(true);

        logger.Messages.Should().NotBeEmpty();
        string.Join("\n", logger.Messages).Should().NotContain("fixturepassword").And.NotContain("fixtureuser").And.NotContain("fixturetoken");
    }

    [Fact]
    public async Task GetAsync_RetryRequiresAnotherRateLimitPermit()
    {
        using var handler = new SequentialResponseHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = new HttpClient(handler);
        using var transport = new ResilientHttpDataTransport(client, new TransportOptions
        {
            PermitLimit = 1,
            QueueLimit = 0,
            RateLimitWindow = TimeSpan.FromMinutes(1),
            MaxRetryAttempts = 2,
            BaseRetryDelay = TimeSpan.Zero,
        });
        var act = () => transport.GetAsync(s_testUri);

        await act.Should().ThrowAsync<RateLimiterRejectedException>().ConfigureAwait(true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetAsync_DefaultClientDoesNotFollowRedirects(bool useFactory)
    {
        await using var server = new RedirectServer();
        var options = NoResilienceOptions();
        options.AllowPrivateNetworks = true;
        using var client = useFactory ? HttpClientFactory.Create("test") : null;
        using var transport = new ResilientHttpDataTransport(client, options);
        var act = () => transport.GetAsync(server.Uri);

        var failure = await act.Should().ThrowAsync<HttpRequestException>().ConfigureAwait(true);
        failure.Which.StatusCode.Should().Be(HttpStatusCode.Found);
        server.RequestCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_TimeoutCoversBodyWithoutChangingBorrowedClient()
    {
        using var handler = new SequentialResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new DelayedStream()),
        });
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var options = NoResilienceOptions();
        options.Timeout = TimeSpan.FromMilliseconds(30);
        using var transport = new ResilientHttpDataTransport(client, options);
        var act = () => transport.GetAsync(s_testUri);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
        client.Timeout.Should().Be(Timeout.InfiniteTimeSpan);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispose_disposes_owned_client(bool asynchronously)
    {
        using var handler = new FakeHttpMessageHandler("ok");
        using var client = new HttpClient(handler);
        var transport = new ResilientHttpDataTransport(client, NoResilienceOptions(), ownsClient: true);
        if (asynchronously)
        {
            await transport.DisposeAsync().ConfigureAwait(true);
        }
        else
        {
            transport.Dispose();
        }

        var act = () => client.GetAsync(s_testUri);

        await act.Should().ThrowAsync<ObjectDisposedException>().ConfigureAwait(true);
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
    public async Task GetAsync_RetryWarningDoesNotLogEchoedCredential()
    {
        const string secret = "retry-warning-fixture-secret";
        using var handler = new SequentialResponseHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = $"Unavailable for https://example.com/data?token={secret}",
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok"),
            });
        using var client = new HttpClient(handler);
        var logger = new RecordingLogger();
        var options = new TransportOptions
        {
            EnableRetry = true,
            MaxRetryAttempts = 1,
            BaseRetryDelay = TimeSpan.Zero,
            EnableRateLimiting = false,
        };
        using var transport = new ResilientHttpDataTransport(client, options, logger);

        using var stream = await transport.GetAsync(new Uri($"https://example.com/data?token={secret}"));
        using var reader = new StreamReader(stream);

        (await reader.ReadToEndAsync()).Should().Be("ok");
        logger.Messages.Should().ContainSingle(message => message.Contains("Retry attempt", StringComparison.Ordinal));
        string.Join("\n", logger.Messages).Should().NotContain(secret);
        string.Join("\n", logger.StructuredValues).Should().NotContain(secret);
        string.Join("\n", logger.Messages).Should().Contain("503");
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

    private sealed class RedirectServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        private int _requestCount;

        public RedirectServer()
        {
            _listener.Start();
            Uri = new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/start");
            _serve = ServeAsync();
        }

        public Uri Uri { get; }
        public int RequestCount => Volatile.Read(ref _requestCount);

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var socket = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(true);
                    await using var stream = socket.GetStream();
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token).ConfigureAwait(true))) { }
                    var count = Interlocked.Increment(ref _requestCount);
                    var response = count == 1
                        ? $"HTTP/1.1 302 Found\r\nLocation: {new Uri(Uri, "/private")}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
                        : "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(true);
            await _serve.ConfigureAwait(true);
            _listener.Stop();
            _stop.Dispose();
        }
    }

    private sealed class DelayedStream : MemoryStream
    {
        public DelayedStream() : base([1]) { }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(150, cancellationToken).ConfigureAwait(true);
            return await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(true);
        }
    }

    private sealed class RecordingLogger : ILogger<ResilientHttpDataTransport>
    {
        public List<string> Messages { get; } = [];
        public List<string> StructuredValues { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                StructuredValues.AddRange(values.Select(value => $"{value.Key}={value.Value}"));
            }
        }
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
