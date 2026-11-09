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

using Boutquin.Domain.Enumerations;
using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Requests;

public sealed class RequestWithRangePreservesInitPropertiesTests
{
    [Fact]
    public void RequestWithRangePreservesInitProperties()
    {
        var types = typeof(IDataRequest).Assembly.GetTypes().Where(type => !type.IsAbstract && typeof(IDataRequest).IsAssignableFrom(type)).ToArray();
        types.Should().HaveCount(10);
        var range = new DateRange(new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 6));
        foreach (var type in types)
        {
            var constructor = type.GetConstructors().OrderBy(ctor => ctor.GetParameters().Length).First();
            var request = (IDataRequest)constructor.Invoke(constructor.GetParameters().Select(parameter => Sample(parameter.ParameterType)).ToArray());
            var properties = type.GetProperties().Where(property => property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit)) == true).ToArray();
            foreach (var property in properties)
            {
                if (property.Name != nameof(IDataRequest.Range)) { property.SetValue(request, Sample(property.PropertyType)); }
            }
            if (request is InstrumentMetadataRequest or OvernightFixingRequest)
            {
                // These two types retain the interface's deliberate opt-out; no narrowing is implemented.
                var unsupported = () => request.WithRange(range);
                unsupported.Should().Throw<NotSupportedException>();
                continue;
            }
            var targetRange = request is YieldCurveQuoteRequest ? new DateRange(range.From, range.From) : range;
            var narrowed = request.WithRange(targetRange);
            narrowed.Range.Should().Be(targetRange);
            foreach (var property in properties.Where(property => property.Name != nameof(IDataRequest.Range) && property.Name != "AsOfDate"))
            {
                property.GetValue(narrowed).Should().BeEquivalentTo(property.GetValue(request), because: type.Name + "." + property.Name);
            }
        }
    }

    private static object Sample(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(PriceBasis)) { return new PriceBasis(PriceAdjustment.All, AdjustedCloseSource.AdapterComputed, PriceAdjustment.Splits); }
        if (type == typeof(DateRange)) { return new DateRange(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 16)); }
        if (type == typeof(DateOnly)) { return new DateOnly(2026, 1, 8); }
        if (type == typeof(string)) { return "SPY"; }
        if (type == typeof(IReadOnlyList<Symbol>)) { return new[] { new Symbol("SPY"), new Symbol("QQQ") }; }
        if (type == typeof(IReadOnlyList<FxPair>)) { return new[] { new FxPair(Currency.EUR, Currency.USD) }; }
        if (type == typeof(ExchangeCode)) { return ExchangeCode.XTSE; }
        if (type.IsEnum) { return Enum.GetValues(type).GetValue(1)!; }
        return type.GetConstructor([typeof(string)])?.Invoke(["SPY"])
            ?? throw new InvalidOperationException("Add a non-default reflection sample for " + type.FullName);
    }
}
