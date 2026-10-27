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
using Boutquin.MarketData.Transport.Http;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Transport.Tests.Unit;

public sealed class Round3UserAgentRegressionTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("Caller/2.0", false)]
    [InlineData(null, true)]
    [InlineData("Caller/2.0", true)]
    public async Task Injected_client_uses_configured_agent_without_mutating_defaults(string? preset, bool explicitHeader)
    {
        using var handler = new CaptureHandler();
        using var client = new HttpClient(handler);
        if (preset is not null)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(preset);
        }

        using var transport = new ResilientHttpDataTransport(client, Options());
        var headers = explicitHeader ? new Dictionary<string, string> { ["uSeR-aGeNt"] = "Explicit/4.0" } : null;
        using var result = await transport.GetAsync(new Uri("https://example.com/data"), headers).ConfigureAwait(true);

        handler.UserAgent.Should().Be(explicitHeader ? "Explicit/4.0" : "Configured/3.0");
        client.DefaultRequestHeaders.UserAgent.ToString().Should().Be(preset ?? "");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_client_preserves_configured_and_explicit_agents(bool explicitHeader)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var capture = CaptureWireAsync(listener);
        using var transport = new ResilientHttpDataTransport(options: Options());
        var headers = explicitHeader ? new Dictionary<string, string> { ["User-Agent"] = "Explicit/4.0" } : null;
        using var result = await transport.GetAsync(new Uri($"http://127.0.0.1:{endpoint.Port}/data"), headers).ConfigureAwait(true);

        (await capture.ConfigureAwait(true)).Should().Be(explicitHeader ? "Explicit/4.0" : "Configured/3.0");
    }

    private static TransportOptions Options() => new()
    {
        UserAgent = "Configured/3.0",
        EnableRetry = false,
        EnableRateLimiting = false,
        AllowPrivateNetworks = true,
    };

    private static async Task<string?> CaptureWireAsync(TcpListener listener)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await listener.AcceptTcpClientAsync(deadline.Token).ConfigureAwait(false);
        await using var stream = socket.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        string? agent = null;
        while (await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false) is { Length: > 0 } line)
        {
            if (line.StartsWith("User-Agent:", StringComparison.OrdinalIgnoreCase))
            {
                agent = line[11..].Trim();
            }
        }

        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"), deadline.Token).ConfigureAwait(false);
        return agent;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        }
    }
}
