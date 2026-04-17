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

using Boutquin.MarketData.Testing.Fakes;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Testing.Tests.Unit;

public sealed class FakeHttpMessageHandlerTests
{
    [Fact]
    public async Task Default_response_is_returned_when_no_mapping_matches()
    {
        using var handler = new FakeHttpMessageHandler("default-body");
        using var client = new HttpClient(handler);

        var response = await client.GetStringAsync("https://example.com/anything");

        response.Should().Be("default-body");
    }

    [Fact]
    public async Task Exact_url_match_takes_priority()
    {
        using var handler = new FakeHttpMessageHandler("default");
        handler.RespondTo("https://example.com/exact", "exact-match");
        using var client = new HttpClient(handler);

        var response = await client.GetStringAsync("https://example.com/exact");

        response.Should().Be("exact-match");
    }

    [Fact]
    public async Task Substring_match_is_used_as_fallback()
    {
        using var handler = new FakeHttpMessageHandler("default");
        handler.RespondTo("api/data", "substring-match");
        using var client = new HttpClient(handler);

        var response = await client.GetStringAsync("https://example.com/api/data?key=123");

        response.Should().Be("substring-match");
    }

    [Fact]
    public async Task AllRequests_tracks_all_sent_requests()
    {
        using var handler = new FakeHttpMessageHandler();
        using var client = new HttpClient(handler);

        await client.GetAsync("https://example.com/a");
        await client.GetAsync("https://example.com/b");

        handler.AllRequests.Should().HaveCount(2);
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://example.com/b");
    }

    [Fact]
    public async Task RespondTo_supports_error_status_codes()
    {
        using var handler = new FakeHttpMessageHandler();
        handler.RespondTo("https://example.com/fail", "error", HttpStatusCode.InternalServerError);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://example.com/fail");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task RespondToWithBytes_returns_binary_content()
    {
        var bytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // ZIP magic
        using var handler = new FakeHttpMessageHandler();
        handler.RespondToWithBytes("https://example.com/file.zip", bytes, "application/zip");
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://example.com/file.zip");
        var content = await response.Content.ReadAsByteArrayAsync();

        content.Should().BeEquivalentTo(bytes);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
    }
}
