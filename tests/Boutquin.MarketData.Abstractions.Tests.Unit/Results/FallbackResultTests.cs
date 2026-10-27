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

using Boutquin.MarketData.Abstractions.Provenance;
using Boutquin.MarketData.Abstractions.Results;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Results;

public sealed class FallbackResultTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Attempts_InputMutation_PreservesAudit(bool useInit)
    {
        var provider = new ProviderCode("backup");
        var attempt = new FallbackAttempt(provider, 1, true, "Accepted");
        var input = new List<FallbackAttempt> { attempt };
        var original = new FallbackResult<string>("payload", provider, []);
        var result = useInit
            ? original with { Attempts = input }
            : new FallbackResult<string>("payload", provider, input);

        input.Clear();

        result.Attempts.Should().Equal(attempt);
        result.Response.Should().Be("payload");
        result.SelectedProviderKey.Should().Be(provider);
        original.Attempts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Attempts_OutboundMutation_IsRefusedAndPreservesActiveReader(bool useInit)
    {
        var provider = new ProviderCode("backup");
        var first = new FallbackAttempt(new ProviderCode("primary"), 2, false, "Rejected");
        var second = new FallbackAttempt(provider, 1, true, "Accepted");
        FallbackAttempt[] input = [first, second];
        var original = new FallbackResult<string>("payload", provider, []);
        var result = useInit
            ? original with { Attempts = input }
            : new FallbackResult<string>("payload", provider, input);
        using var reader = result.Attempts.GetEnumerator();
        reader.MoveNext().Should().BeTrue();
        reader.Current.Should().Be(first);

        if (result.Attempts is IList<FallbackAttempt> mutable)
        {
            var mutate = () => mutable[1] = first;
            mutate.Should().Throw<NotSupportedException>();
        }

        reader.MoveNext().Should().BeTrue();
        reader.Current.Should().Be(second);
        reader.MoveNext().Should().BeFalse();
        result.Attempts.Should().Equal(first, second);
    }
}
