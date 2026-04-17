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
/// Configuration for <see cref="ResilientHttpDataTransport"/> resilience behavior.
/// </summary>
public sealed class TransportOptions
{
    /// <summary>
    /// User-Agent header value sent with every request.
    /// </summary>
    public string UserAgent { get; set; } = "Boutquin.MarketData/1.0";

    /// <summary>
    /// HTTP request timeout per attempt.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether to enable automatic retry on transient failures.
    /// </summary>
    public bool EnableRetry { get; set; } = true;

    /// <summary>
    /// Maximum number of retry attempts (excludes the initial attempt).
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay before the first retry. Subsequent retries use exponential backoff with jitter.
    /// </summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Whether to enable sliding-window rate limiting.
    /// </summary>
    public bool EnableRateLimiting { get; set; } = true;

    /// <summary>
    /// Maximum number of permits in the rate-limit window.
    /// </summary>
    public int PermitLimit { get; set; } = 20;

    /// <summary>
    /// Maximum number of requests that can queue when the rate limit is reached.
    /// </summary>
    public int QueueLimit { get; set; } = 100;

    /// <summary>
    /// Duration of the sliding rate-limit window.
    /// </summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Number of segments dividing the sliding window for smoother rate distribution.
    /// </summary>
    public int SegmentsPerWindow { get; set; } = 4;

    /// <summary>
    /// When <see langword="false"/> (the default), <see cref="ResilientHttpDataTransport.GetAsync"/>
    /// rejects requests to loopback, link-local, and RFC-1918 private-network addresses as SSRF
    /// defense. Set to <see langword="true"/> only for test harnesses that must reach localhost
    /// or VPN-resolved private hosts.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>
    /// Hard upper bound on the response body size accepted by a single
    /// <see cref="ResilientHttpDataTransport.GetAsync"/> call. Defaults to 32 MiB. A misbehaving
    /// or malicious server that streams a multi-GB body will be aborted once the buffered size
    /// exceeds this limit, preventing memory exhaustion. Zero disables the check (not recommended).
    /// </summary>
    public long MaxResponseBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>
    /// Query-string parameter names whose values are redacted in structured logs. Data providers
    /// commonly embed secrets (<c>?token=…</c>, <c>?api_key=…</c>) in URLs; logging the full URI
    /// at Debug level would leak credentials. Matches are case-insensitive.
    /// </summary>
    public IReadOnlyCollection<string> SensitiveQueryParameters { get; set; } =
        ["token", "apikey", "api_key", "apitoken", "api_token", "secret", "access_token", "auth"];

    /// <summary>
    /// Connection pool lifetime for the default HTTP client. Expired connections are recycled so
    /// the process picks up DNS changes rather than pinning to a stale IP indefinitely. Defaults
    /// to two minutes, matching the guidance in <see cref="System.Net.Http.SocketsHttpHandler"/>
    /// documentation for long-lived clients.
    /// </summary>
    public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(2);
}
