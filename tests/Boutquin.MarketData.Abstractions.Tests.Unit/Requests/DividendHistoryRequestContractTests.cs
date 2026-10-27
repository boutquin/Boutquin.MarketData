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

using System.Collections;
using System.Reflection;

using Boutquin.MarketData.Abstractions.Contracts;
using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Abstractions.Requests;
using FluentAssertions;
using Xunit;

namespace Boutquin.MarketData.Abstractions.Tests.Unit.Requests;

/// <summary>
/// Public-shape contract pin for <c>DividendHistoryRequest</c>.
/// </summary>
/// <remarks>
/// These assertions are deliberately reflective rather than strongly typed. The last test
/// mirrors, step for step, the reflective algorithm the orchestration package's batch-request
/// splitter runs against a request before the time-series store builds a dataset key: it is the
/// only way to prove from this package that the request is actually consumable by that path
/// (the splitter itself is internal to the orchestration assembly, which this test project does
/// not reference). A strongly typed test would prove the type compiles, not that the production
/// consumer can partition it.
/// </remarks>
public sealed class DividendHistoryRequestContractTests
{
    private const string RequestTypeName =
        "Boutquin.MarketData.Abstractions.Requests.DividendHistoryRequest";

    // The compiler-generated record clone method the batch splitter invokes.
    private const string RecordCloneMethodName = "<Clone>$";

    private static readonly DateRange s_range = new(
        new DateOnly(2024, 1, 1),
        new DateOnly(2024, 12, 31));

    private static Type? RequestType =>
        typeof(PriceHistoryRequest).Assembly.GetType(RequestTypeName, throwOnError: false);

    private static Type RequireRequestType()
    {
        var type = RequestType;
        type.Should().NotBeNull(
            because: "the dividend fetch path needs a typed request in the Abstractions "
                + $"package; expected {RequestTypeName} to exist");
        return type!;
    }

    private static object NewRequest(params string[] tickers)
    {
        var symbols = tickers.Select(t => new Symbol(t)).ToList();
        return Activator.CreateInstance(RequireRequestType(), symbols, s_range)!;
    }

    [Fact]
    public void Type_exists_in_the_Requests_namespace_of_the_Abstractions_assembly()
    {
        var type = RequireRequestType();

        type.IsSealed.Should().BeTrue(because: "every sibling request record is sealed");
        type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance)
            .Should().NotBeNull(because: "the request must be a record so the batch splitter can clone it");
    }

    [Fact]
    public void Type_implements_IDataRequest_and_IBatchDataRequest_of_Symbol()
    {
        var type = RequireRequestType();

        typeof(IDataRequest).IsAssignableFrom(type).Should().BeTrue();
        typeof(IBatchDataRequest<Symbol>).IsAssignableFrom(type).Should().BeTrue(
            because: "dividends are per-symbol, so the request partitions over Symbol like prices do");
    }

    [Fact]
    public void Type_exposes_the_agreed_member_set()
    {
        var type = RequireRequestType();

        // Symbols is settable (init) because the batch splitter overwrites it on a record clone.
        var symbols = type.GetProperty("Symbols");
        symbols.Should().NotBeNull();
        symbols!.PropertyType.Should().Be(typeof(IReadOnlyList<Symbol>));
        symbols.CanWrite.Should().BeTrue(because: "the batch splitter writes the single-partition symbol list");

        var exchange = type.GetProperty("Exchange");
        exchange.Should().NotBeNull();
        exchange!.PropertyType.Should().Be(typeof(ExchangeCode?));
        exchange.CanWrite.Should().BeTrue(because: "Exchange is an init property for binary compatibility");

        type.GetProperty("Range")!.PropertyType.Should().Be(typeof(DateRange));
        type.GetProperty("DatasetKey")!.PropertyType.Should().Be(typeof(string));
        type.GetProperty("PartitionKeys")!.PropertyType.Should().Be(typeof(IReadOnlyList<Symbol>));
        type.GetProperty("Frequency")!.PropertyType.Should().Be(typeof(DataFrequency));

        type.GetMethod("WithRange", [typeof(DateRange)])
            .Should().NotBeNull(because: "the request opts into the time-series gap-fill path");
    }

    [Fact]
    public void PartitionKeys_satisfies_the_batch_splitter_partitioning_contract()
    {
        var request = NewRequest("MSFT", "AAPL");
        var requestType = request.GetType();

        // Step 1 — the splitter locates the IBatchDataRequest<> interface.
        var batchInterface = Array.Find(
            requestType.GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBatchDataRequest<>));
        batchInterface.Should().NotBeNull();

        // Step 2 — it reads PartitionKeys and requires an enumerable of more than one key.
        var partitionKeysValue = batchInterface!
            .GetProperty(nameof(IBatchDataRequest<object>.PartitionKeys))!
            .GetValue(request);
        partitionKeysValue.Should().BeAssignableTo<IEnumerable>();
        var keys = ((IEnumerable)partitionKeysValue!).Cast<object>().ToList();
        keys.Should().HaveCount(2);

        // Step 3 — it needs a writable property whose current value IS the PartitionKeys object.
        // This is where a request that projects a fresh list from PartitionKeys (rather than
        // returning the backing property) fails: the splitter throws InvalidOperationException.
        var partitionListProperty = Array.Find(
            requestType.GetProperties(),
            p => p.CanWrite && ReferenceEquals(p.GetValue(request), partitionKeysValue));
        partitionListProperty.Should().NotBeNull(
            because: "the splitter overwrites the list property backing PartitionKeys; without a "
                + "reference-equal writable property it throws rather than partitioning");
        partitionListProperty!.Name.Should().Be("Symbols");

        // Step 4 — it clones the record and overwrites the list with a single-key array.
        var cloneMethod = requestType.GetMethod(RecordCloneMethodName);
        cloneMethod.Should().NotBeNull();

        var singleKeyList = Array.CreateInstance(batchInterface.GetGenericArguments()[0], 1);
        singleKeyList.SetValue(keys[0], 0);
        var part = cloneMethod!.Invoke(request, null)!;
        partitionListProperty.SetValue(part, singleKeyList);

        // Step 5 — the produced part is a usable single-partition request.
        var partKeys = (IReadOnlyList<Symbol>)requestType.GetProperty("PartitionKeys")!.GetValue(part)!;
        partKeys.Should().ContainSingle().Which.Should().Be((Symbol)keys[0]);
        requestType.GetProperty("Range")!.GetValue(part).Should().Be(s_range);
        ((string)requestType.GetProperty("DatasetKey")!.GetValue(part)!)
            .Should().Be("MSFT", because: "a partitioned request keys on its own single symbol");
    }
}
