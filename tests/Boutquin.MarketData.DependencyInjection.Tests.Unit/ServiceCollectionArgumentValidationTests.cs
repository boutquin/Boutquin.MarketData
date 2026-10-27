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

using Boutquin.MarketData.Abstractions.Records;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

public sealed class ServiceCollectionArgumentValidationTests
{
    [Fact]
    public void AddMarketDataKernel_NullServices_ThrowsForPublicParameter()
    {
        IServiceCollection services = null!;

        var act = () => services.AddMarketDataKernel();

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("services");
    }

    [Fact]
    public void AddMarketDataKernel_NonNullServices_Composes()
    {
        var services = new ServiceCollection();

        var result = services.AddMarketDataKernel();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddMarketDataWriteThroughFor_NullServices_ThrowsForPublicParameter()
    {
        IServiceCollection services = null!;

        Func<IServiceCollection> act = services.AddMarketDataWriteThroughFor<Bar>;

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("services");
    }

    [Fact]
    public void AddMarketDataWriteThroughFor_NonNullServices_Composes()
    {
        var services = new ServiceCollection();

        var result = services.AddMarketDataWriteThroughFor<Bar>();

        result.Should().BeSameAs(services);
    }
}
