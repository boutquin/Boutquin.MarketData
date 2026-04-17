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
using Boutquin.MarketData.Transport.Http;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Transport.Tests.Unit;

public sealed class HttpClientFactoryTests
{
    [Fact]
    public void Create_sets_default_timeout_to_30_seconds()
    {
        using var client = HttpClientFactory.Create("TestProduct");

        client.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Create_sets_custom_timeout()
    {
        using var client = HttpClientFactory.Create("TestProduct", timeout: TimeSpan.FromSeconds(10));

        client.Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Create_sets_user_agent_without_contact_url()
    {
        using var client = HttpClientFactory.Create("MyApp");

        var userAgent = client.DefaultRequestHeaders.UserAgent.ToString();
        userAgent.Should().Contain("MyApp/0.1");
        userAgent.Should().NotContain("+");
    }

    [Fact]
    public void Create_sets_user_agent_with_contact_url()
    {
        using var client = HttpClientFactory.Create("MyApp", "https://example.com/repo");

        var userAgent = client.DefaultRequestHeaders.UserAgent.ToString();
        userAgent.Should().Contain("MyApp/0.1");
        userAgent.Should().Contain("+https://example.com/repo");
    }

    [Fact]
    public void Create_sets_accept_headers()
    {
        using var client = HttpClientFactory.Create("TestProduct");

        var accept = client.DefaultRequestHeaders.Accept.ToString();
        accept.Should().Contain("application/json");
        accept.Should().Contain("text/xml");
        accept.Should().Contain("application/xml");
    }
}
