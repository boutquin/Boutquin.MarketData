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

public sealed class TransportOptionsTests
{
    [Fact]
    public void Default_UserAgent_is_correct()
    {
        var opts = new TransportOptions();

        opts.UserAgent.Should().Be("Boutquin.MarketData/1.0");
    }

    [Fact]
    public void Default_Timeout_is_30_seconds()
    {
        var opts = new TransportOptions();

        opts.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Default_EnableRetry_is_true()
    {
        var opts = new TransportOptions();

        opts.EnableRetry.Should().BeTrue();
    }

    [Fact]
    public void Default_MaxRetryAttempts_is_3()
    {
        var opts = new TransportOptions();

        opts.MaxRetryAttempts.Should().Be(3);
    }

    [Fact]
    public void Default_BaseRetryDelay_is_500ms()
    {
        var opts = new TransportOptions();

        opts.BaseRetryDelay.Should().Be(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void Default_EnableRateLimiting_is_true()
    {
        var opts = new TransportOptions();

        opts.EnableRateLimiting.Should().BeTrue();
    }

    [Fact]
    public void Default_PermitLimit_is_20()
    {
        var opts = new TransportOptions();

        opts.PermitLimit.Should().Be(20);
    }

    [Fact]
    public void Default_QueueLimit_is_100()
    {
        var opts = new TransportOptions();

        opts.QueueLimit.Should().Be(100);
    }

    [Fact]
    public void Default_RateLimitWindow_is_1_second()
    {
        var opts = new TransportOptions();

        opts.RateLimitWindow.Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Default_SegmentsPerWindow_is_4()
    {
        var opts = new TransportOptions();

        opts.SegmentsPerWindow.Should().Be(4);
    }

    [Fact]
    public void Property_setters_work()
    {
        var opts = new TransportOptions
        {
            UserAgent = "Custom/2.0",
            Timeout = TimeSpan.FromSeconds(60),
            EnableRetry = false,
            MaxRetryAttempts = 5,
            BaseRetryDelay = TimeSpan.FromSeconds(1),
            EnableRateLimiting = false,
            PermitLimit = 50,
            QueueLimit = 200,
            RateLimitWindow = TimeSpan.FromSeconds(10),
            SegmentsPerWindow = 8,
        };

        opts.UserAgent.Should().Be("Custom/2.0");
        opts.Timeout.Should().Be(TimeSpan.FromSeconds(60));
        opts.EnableRetry.Should().BeFalse();
        opts.MaxRetryAttempts.Should().Be(5);
        opts.BaseRetryDelay.Should().Be(TimeSpan.FromSeconds(1));
        opts.EnableRateLimiting.Should().BeFalse();
        opts.PermitLimit.Should().Be(50);
        opts.QueueLimit.Should().Be(200);
        opts.RateLimitWindow.Should().Be(TimeSpan.FromSeconds(10));
        opts.SegmentsPerWindow.Should().Be(8);
    }
}
