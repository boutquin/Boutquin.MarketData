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

using Boutquin.MarketData.Abstractions.ReferenceData;
using Boutquin.MarketData.Storage.Serializers;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Storage.Tests.Unit.Serializers;

public sealed class JsonSnapshotSerializerTests
{
    [Fact]
    public async Task SerializeAsync_null_reference_rejects_before_writing()
    {
        await AssertNullRejectedAsync<string?>();
    }

    [Fact]
    public async Task SerializeAsync_null_nullable_value_rejects_before_writing()
    {
        await AssertNullRejectedAsync<int?>();
    }

    private static async Task AssertNullRejectedAsync<T>()
    {
        var serializer = new JsonSnapshotSerializer<T>();
        using var stream = new MemoryStream();
        stream.Write([1, 2, 3]);
        var act = () => serializer.SerializeAsync(stream, default!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("data");
        stream.ToArray().Should().Equal(1, 2, 3);
        stream.Position.Should().Be(3);
    }

    private sealed record InstrumentShape(Symbol Symbol, string Name, DateOnly? Inception);

    [Fact]
    public async Task RoundTrip_preserves_Symbol_value_on_nested_record()
    {
        // Regression: camelCase JSON ("symbol":{"ticker":"AGG"}) round-tripped through
        // JsonSnapshotSerializer must populate Symbol.Ticker, not fall back to default(Symbol).
        var original = new InstrumentShape(
            new Symbol("AGG"),
            "iShares Core U.S. Aggregate Bond ETF",
            new DateOnly(2003, 9, 26));
        var serializer = new JsonSnapshotSerializer<InstrumentShape>();

        using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, original).ConfigureAwait(true);
        stream.Position = 0;
        var roundTripped = await serializer.DeserializeAsync(stream).ConfigureAwait(true);

        roundTripped.Symbol.Ticker.Should().Be("AGG");
        roundTripped.Name.Should().Be(original.Name);
        roundTripped.Inception.Should().Be(original.Inception);
    }
}
