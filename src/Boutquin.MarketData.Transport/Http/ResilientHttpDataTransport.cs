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
using System.Security.Authentication;
using System.Text;
using System.Threading.RateLimiting;
using System.Web;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Polly;
using Polly.Retry;

namespace Boutquin.MarketData.Transport.Http;

/// <summary>
/// HTTP data transport with Polly v8 resilience: exponential-backoff retry with jitter,
/// optional sliding-window rate limiting, and defense-in-depth against SSRF and resource
/// exhaustion.
/// </summary>
/// <remarks>
/// <para>
/// Retries only on transient failures (network errors, 5xx, 429). Non-retryable failures
/// (4xx status codes, URI format errors) propagate immediately. The delay between retries
/// uses exponential backoff with jitter via Polly's built-in decorrelated jitter strategy.
/// </para>
/// <para>
/// When rate limiting is enabled, a <see cref="SlidingWindowRateLimiter"/> governs outbound
/// request throughput. Requests exceeding the permit limit queue up to <see cref="TransportOptions.QueueLimit"/>
/// before being rejected.
/// </para>
/// <para>
/// The response body is copied into a <see cref="MemoryStream"/> before returning so
/// the underlying HTTP connection is released promptly. The copy is bounded by
/// <see cref="TransportOptions.MaxResponseBytes"/>; payloads above the bound are rejected
/// to prevent memory exhaustion from misbehaving or malicious servers.
/// </para>
/// <para>
/// <strong>SSRF defense.</strong> By default, requests to loopback, link-local, and RFC-1918
/// private-network addresses are rejected before any HTTP call is issued. Set
/// <see cref="TransportOptions.AllowPrivateNetworks"/> to opt into localhost testing.
/// </para>
/// </remarks>
public sealed class ResilientHttpDataTransport : IHttpDataTransport, IDisposable, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ResiliencePipeline<Stream> _pipeline;
    private readonly RateLimiter? _rateLimiter;
    private readonly ILogger<ResilientHttpDataTransport> _logger;
    private readonly bool _ownsClient;
    private readonly bool _allowPrivateNetworks;
    private readonly long _maxResponseBytes;
    private readonly IReadOnlySet<string> _sensitiveQueryParameters;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="ResilientHttpDataTransport"/>.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for requests. If <see langword="null"/>, a new client is created and owned.</param>
    /// <param name="options">Transport configuration. If <see langword="null"/>, defaults are used.</param>
    /// <param name="logger">Logger for structured diagnostics.</param>
    /// <param name="ownsClient">Whether this instance owns and should dispose the HTTP client. Ignored when <paramref name="httpClient"/> is <see langword="null"/>.</param>
    public ResilientHttpDataTransport(
        HttpClient? httpClient = null,
        TransportOptions? options = null,
        ILogger<ResilientHttpDataTransport>? logger = null,
        bool ownsClient = false)
    {
        var opts = options ?? new TransportOptions();
        _logger = logger ?? NullLogger<ResilientHttpDataTransport>.Instance;
        _httpClient = httpClient ?? CreateDefaultClient(opts);
        _ownsClient = httpClient is null || ownsClient;
        _allowPrivateNetworks = opts.AllowPrivateNetworks;
        _maxResponseBytes = opts.MaxResponseBytes;
        _sensitiveQueryParameters = new HashSet<string>(
            opts.SensitiveQueryParameters,
            StringComparer.OrdinalIgnoreCase);

        // Only apply the transport timeout to clients we own. Overwriting a caller-owned
        // HttpClient's Timeout silently mutates shared state — the caller may rely on their
        // own value (e.g. streaming-download clients configured with InfiniteTimeSpan).
        if (httpClient is null)
        {
            _httpClient.Timeout = opts.Timeout;
        }

        _rateLimiter = opts.EnableRateLimiting
            ? new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = opts.PermitLimit,
                QueueLimit = opts.QueueLimit,
                Window = opts.RateLimitWindow,
                SegmentsPerWindow = opts.SegmentsPerWindow,
                AutoReplenishment = true,
            })
            : null;

        _pipeline = BuildPipeline(opts);
    }

    /// <inheritdoc />
    public async Task<Stream> GetAsync(
        Uri uri,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ValidateUri(uri);

        return await _pipeline.ExecuteAsync(async ct =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            if (headers is not null)
            {
                foreach (var (key, value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }

            _logger.LogDebug("HTTP GET {Uri}", RedactUri(uri));

            // using ensures the response (and its underlying connection) is released on every
            // exit path — success, exception from EnsureSuccessStatusCode, or buffer-cap abort.
            // The prior `var response` without disposal leaked connections on failures.
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            {
                response.EnsureSuccessStatusCode();

                // Short-circuit when the advertised body size already exceeds the cap so we
                // never allocate the MemoryStream for a hostile payload.
                if (_maxResponseBytes > 0
                    && response.Content.Headers.ContentLength is { } advertised
                    && advertised > _maxResponseBytes)
                {
                    throw new HttpRequestException(
                        $"Response body ({advertised:N0} bytes) exceeds configured maximum of {_maxResponseBytes:N0} bytes.");
                }

                var capacity = response.Content.Headers.ContentLength is { } len && len > 0 && len <= _maxResponseBytes
                    ? (int)len
                    : 0;
                var memoryStream = new MemoryStream(capacity);

                var responseStream = await response.Content
                    .ReadAsStreamAsync(ct)
                    .ConfigureAwait(false);

                await using (responseStream.ConfigureAwait(false))
                {
                    var buffer = new byte[81920];
                    var total = 0L;
                    int read;
                    while ((read = await responseStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (_maxResponseBytes > 0 && total > _maxResponseBytes)
                        {
                            await memoryStream.DisposeAsync().ConfigureAwait(false);
                            throw new HttpRequestException(
                                $"Response body exceeded configured maximum of {_maxResponseBytes:N0} bytes while streaming.");
                        }

                        await memoryStream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    }
                }

                memoryStream.Position = 0;

                _logger.LogDebug("HTTP GET {Uri} completed ({StatusCode}, {Bytes} bytes)",
                    RedactUri(uri), (int)response.StatusCode, memoryStream.Length);

                return (Stream)memoryStream;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Interlocked guards against re-entry; SlidingWindowRateLimiter throws
        // ObjectDisposedException on double-dispose.
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _rateLimiter?.Dispose();
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (_rateLimiter is not null)
        {
            await _rateLimiter.DisposeAsync().ConfigureAwait(false);
        }

        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// Validates the request URI against SSRF policy. Extracted so callers of future public
    /// request-style APIs can share the same guard.
    /// </summary>
    private void ValidateUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            throw new ArgumentException("URI must be absolute.", nameof(uri));
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                $"Only http and https schemes are supported (got '{uri.Scheme}').",
                nameof(uri));
        }

        if (_allowPrivateNetworks)
        {
            return;
        }

        if (IPAddress.TryParse(uri.Host, out var literal) && IsPrivateOrLoopback(literal))
        {
            throw new ArgumentException(
                $"SSRF policy rejects request to private/loopback address '{uri.Host}'. Set TransportOptions.AllowPrivateNetworks to override.",
                nameof(uri));
        }

        // DNS-literal hostnames for obvious internal targets — cheap pre-flight.
        if (uri.IsLoopback
            || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"SSRF policy rejects request to loopback/internal host '{uri.Host}'. Set TransportOptions.AllowPrivateNetworks to override.",
                nameof(uri));
        }
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();

        // IPv4 private ranges: 10/8, 172.16/12, 192.168/16, 169.254/16 (link-local),
        // 100.64/10 (CGNAT).
        if (address.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                || bytes[0] == 0
                || bytes[0] >= 224;
        }

        // IPv6: link-local (fe80::/10) and ULA (fc00::/7).
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            return (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                || (bytes[0] & 0xfe) == 0xfc
                || IPAddress.IPv6Loopback.Equals(address);
        }

        return false;
    }

    /// <summary>
    /// Returns a log-safe rendition of <paramref name="uri"/> with sensitive query-parameter
    /// values replaced by <c>***</c>.
    /// </summary>
    private string RedactUri(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query))
        {
            return uri.ToString();
        }

        var parsed = HttpUtility.ParseQueryString(uri.Query);
        var needsRedaction = false;
        foreach (var key in parsed.AllKeys)
        {
            if (key is not null && _sensitiveQueryParameters.Contains(key))
            {
                parsed[key] = "***";
                needsRedaction = true;
            }
        }

        if (!needsRedaction)
        {
            return uri.ToString();
        }

        var builder = new StringBuilder(uri.GetLeftPart(UriPartial.Path));
        builder.Append('?').Append(parsed);
        if (!string.IsNullOrEmpty(uri.Fragment))
        {
            builder.Append(uri.Fragment);
        }

        return builder.ToString();
    }

    private ResiliencePipeline<Stream> BuildPipeline(TransportOptions opts)
    {
        var builder = new ResiliencePipelineBuilder<Stream>();

        if (opts.EnableRateLimiting && _rateLimiter is not null)
        {
            builder.AddRateLimiter(_rateLimiter);
        }

        if (opts.EnableRetry)
        {
            builder.AddRetry(new RetryStrategyOptions<Stream>
            {
                MaxRetryAttempts = opts.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = opts.BaseRetryDelay,
                ShouldHandle = new PredicateBuilder<Stream>()
                    .Handle<HttpRequestException>(IsTransientHttpException),
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "Retry attempt {AttemptNumber}/{MaxRetries} after {Delay:N0}ms for HTTP request (reason: {Exception})",
                        args.AttemptNumber, opts.MaxRetryAttempts, args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? "non-success result");
                    return ValueTask.CompletedTask;
                },
            });
        }

        return builder.Build();
    }

    /// <summary>
    /// Narrow predicate: retry only on transport-level failures (network/socket/TLS errors,
    /// 5xx, 408, 429). Programmer errors (malformed URI, invalid headers) fail fast rather
    /// than wasting retry attempts on non-recoverable failures.
    /// </summary>
    private static bool IsTransientHttpException(HttpRequestException ex)
    {
        if (ex.StatusCode is { } status)
        {
            return (int)status >= 500
                || status == HttpStatusCode.RequestTimeout
                || status == HttpStatusCode.TooManyRequests;
        }

        // No StatusCode: network-level failure. Only retry when the inner exception is a
        // genuine transport issue. Anything else (UriFormatException, InvalidOperationException)
        // points at a caller bug.
        return ex.InnerException is SocketException
            or IOException
            or AuthenticationException
            or TimeoutException;
    }

    private static HttpClient CreateDefaultClient(TransportOptions opts)
    {
        // PooledConnectionLifetime causes sockets to be recycled so the process picks up DNS
        // changes. Without this, long-lived HttpClients pin to stale IPs forever — a common
        // production footgun when data providers use DNS-based failover.
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = opts.PooledConnectionLifetime,
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = opts.Timeout,
            MaxResponseContentBufferSize = opts.MaxResponseBytes > 0 ? opts.MaxResponseBytes : int.MaxValue,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(opts.UserAgent);
        return client;
    }
}
