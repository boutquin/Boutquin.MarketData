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
using Boutquin.MarketData.Cache.Keys;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Cache.Tests.Unit;

public sealed class CacheKeyBuilderTests
{
    [Fact]
    public void Segment_boundaries_are_part_of_key_identity()
    {
        CacheKeyBuilder.FromSegments("p", ["a/b", "c"]).Should()
            .NotBe(CacheKeyBuilder.FromSegments("p", ["a", "b/c"]));
        CacheKeyBuilder.FromSegments("p", []).Should()
            .NotBe(CacheKeyBuilder.FromSegments("p", [string.Empty]));
    }

    [Fact]
    public void Prefix_and_identifier_boundaries_are_part_of_range_identity()
    {
        CacheKeyBuilder.FromDateRange("a/b", "c", null, null).Should()
            .NotBe(CacheKeyBuilder.FromDateRange("a", "b/c", null, null));
    }

    [Fact]
    public void FromSegments_sorts_and_joins_segments()
    {
        var key = CacheKeyBuilder.FromSegments("market", new[] { "MSFT", "AAPL", "GOOG" });

        key.Should().Be("cache-v2/segments/006d00610072006b00650074/z/004100410050004c/z/0047004f004f0047/z/004d005300460054/z/end");
    }

    [Fact]
    public void FromSegments_is_order_independent()
    {
        var key1 = CacheKeyBuilder.FromSegments("fx", new[] { "EUR/USD", "GBP/USD" });
        var key2 = CacheKeyBuilder.FromSegments("fx", new[] { "GBP/USD", "EUR/USD" });

        key1.Should().Be(key2);
    }

    [Fact]
    public void FromSegments_handles_single_segment()
    {
        var key = CacheKeyBuilder.FromSegments("data", new[] { "AAPL" });

        key.Should().Be("cache-v2/segments/0064006100740061/z/004100410050004c/z/end");
    }

    [Fact]
    public void FromSegments_handles_empty_segments()
    {
        var key = CacheKeyBuilder.FromSegments("data", Array.Empty<string>());

        key.Should().Be("cache-v2/segments/0064006100740061/z/end");
    }

    [Fact]
    public void FromDateRange_with_both_dates()
    {
        var from = new DateOnly(2024, 1, 15);
        var to = new DateOnly(2024, 12, 31);

        var key = CacheKeyBuilder.FromDateRange("fred", "DGS10", from, to);

        key.Should().Be("cache-v2/range/0066007200650064/z/00440047005300310030/z/2024-01-15/2024-12-31");
    }

    [Fact]
    public void FromDateRange_with_null_from_uses_wildcard()
    {
        var key = CacheKeyBuilder.FromDateRange("fred", "DGS10", null, new DateOnly(2024, 12, 31));

        key.Should().Be("cache-v2/range/0066007200650064/z/00440047005300310030/z/_/2024-12-31");
    }

    [Fact]
    public void FromDateRange_with_null_to_uses_wildcard()
    {
        var key = CacheKeyBuilder.FromDateRange("fred", "DGS10", new DateOnly(2024, 1, 1), null);

        key.Should().Be("cache-v2/range/0066007200650064/z/00440047005300310030/z/2024-01-01/_");
    }

    [Fact]
    public void FromDateRange_with_both_null_uses_wildcards()
    {
        var key = CacheKeyBuilder.FromDateRange("fred", "DGS10", null, null);

        key.Should().Be("cache-v2/range/0066007200650064/z/00440047005300310030/z/_/_");
    }
}
