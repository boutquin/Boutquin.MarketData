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

using Boutquin.MarketData.Abstractions.Exceptions;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Exceptions;

public sealed class MarketDataExceptionTests
{
    public static IEnumerable<object[]> ConcreteTypes =>
    [
        [typeof(MarketDataNotFoundException)],
        [typeof(MarketDataRetrievalException)],
        [typeof(MarketDataProcessingException)],
        [typeof(MarketDataStorageException)],
        [typeof(SymbolNotFoundException)],
    ];

    [Fact]
    public void Base_is_abstract()
    {
        typeof(MarketDataException).IsAbstract.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ConcreteTypes))]
    public void Concrete_derives_from_base(Type t)
    {
        typeof(MarketDataException).IsAssignableFrom(t).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ConcreteTypes))]
    public void Concrete_is_sealed(Type t)
    {
        t.IsSealed.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(ConcreteTypes))]
    public void Concrete_supports_message_constructor(Type t)
    {
        var ctor = t.GetConstructor([typeof(string)]);

        ctor.Should().NotBeNull();

        var ex = (Exception)ctor!.Invoke(["probe"]);
        ex.Message.Should().Be("probe");
    }

    [Theory]
    [MemberData(nameof(ConcreteTypes))]
    public void Concrete_supports_message_and_inner_exception_constructor(Type t)
    {
        var ctor = t.GetConstructor([typeof(string), typeof(Exception)]);

        ctor.Should().NotBeNull();

        var inner = new InvalidOperationException("root");
        var ex = (Exception)ctor!.Invoke(["probe", inner]);
        ex.Message.Should().Be("probe");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void Catching_base_captures_every_concrete_type()
    {
        Exception captured;
        try
        {
            throw new MarketDataNotFoundException("x");
        }
        catch (MarketDataException ex)
        {
            captured = ex;
        }

        captured.Should().BeOfType<MarketDataNotFoundException>();
    }
}
