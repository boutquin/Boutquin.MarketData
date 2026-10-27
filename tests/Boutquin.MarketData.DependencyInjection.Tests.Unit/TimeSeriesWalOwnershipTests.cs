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

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.TimeSeries;

using Boutquin.Storage.Domain.Enums;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

/// <summary>
/// Pins ownership of the write-ahead log allocated by the MarketData time-series builders.
/// Storage deliberately treats an injected WAL as caller-owned, so the MarketData-owned engine
/// surface must release both resources before a same-root restart.
/// </summary>
public sealed class TimeSeriesWalOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "mktdata-wal-owner-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task ColumnarEngine_Dispose_ReleasesWalForImmediateReopen()
    {
        var first = await ColumnarBuilder().BuildStackAsync().ConfigureAwait(true);
        ((IDisposable)first.Engine).Dispose();

        var second = await ColumnarBuilder().BuildStackAsync().ConfigureAwait(true);
        await ((IAsyncDisposable)second.Engine).DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ColumnarEngine_DisposeAsync_ReleasesWalForImmediateReopen()
    {
        var first = await ColumnarBuilder().BuildStackAsync().ConfigureAwait(true);
        await ((IAsyncDisposable)first.Engine).DisposeAsync().ConfigureAwait(true);

        var second = await ColumnarBuilder().BuildStackAsync().ConfigureAwait(true);
        ((IDisposable)second.Engine).Dispose();
    }

    [Fact]
    public async Task RowEngine_Dispose_ReleasesWalForImmediateReopen()
    {
        var first = await BuildRowAsync().ConfigureAwait(true);
        ((IDisposable)first.Engine).Dispose();

        var second = await BuildRowAsync().ConfigureAwait(true);
        await ((IAsyncDisposable)second.Engine).DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RowEngine_DisposeAsync_ReleasesWalForImmediateReopen()
    {
        var first = await BuildRowAsync().ConfigureAwait(true);
        await ((IAsyncDisposable)first.Engine).DisposeAsync().ConfigureAwait(true);

        var second = await BuildRowAsync().ConfigureAwait(true);
        ((IDisposable)second.Engine).Dispose();
    }

    [Fact]
    public void ServiceProvider_Dispose_ReleasesEveryRealizedWalForImmediateReopen()
    {
        using (var first = BuildProvider())
        {
            _ = first.GetRequiredService<ITimeSeriesStoreRegistry>();
        }

        using var second = BuildProvider();
        _ = second.GetRequiredService<ITimeSeriesStoreRegistry>();
    }

    [Fact]
    public async Task ServiceProvider_DisposeAsync_ReleasesEveryRealizedWalForImmediateReopen()
    {
        var first = BuildProvider();
        _ = first.GetRequiredService<ITimeSeriesStoreRegistry>();
        await first.DisposeAsync().ConfigureAwait(true);

        await using var second = BuildProvider();
        _ = second.GetRequiredService<ITimeSeriesStoreRegistry>();
    }

    [Fact]
    public async Task CancelledBuild_ReleasesAllocatedWalForRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancelled = async () => await ColumnarBuilder()
            .BuildStackAsync(cts.Token)
            .ConfigureAwait(true);

        await cancelled.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);

        var retry = await ColumnarBuilder().BuildStackAsync().ConfigureAwait(true);
        await ((IAsyncDisposable)retry.Engine).DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CancelledRowBuild_ReleasesAllocatedWalForRetry()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancelled = async () => await RowTimeSeriesStoreFactory
            .BuildStackAsync<DividendPaymentDatasetKey, DividendPayment>(
                nameof(DividendPayment),
                RowOptions(),
                cts.Token)
            .ConfigureAwait(true);

        await cancelled.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);

        var retry = await BuildRowAsync().ConfigureAwait(true);
        await ((IAsyncDisposable)retry.Engine).DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FailedOpen_ReleasesAllocatedWalForEquivalentRetry()
    {
        var builder = ColumnarBuilder();
        var initial = await builder.BuildStackAsync().ConfigureAwait(true);
        var key = new PriceBarDatasetKey("default", DataFrequency.Daily, "AGG", null);
        var start = new DateOnly(2022, 1, 1);
        var rows = Enumerable.Range(0, 300)
            .Select(i => new Bar(start.AddDays(i), 100m + i, 100m + i, 100m + i, 100m + i, 100m + i, 1_000))
            .ToArray();

        await initial.Store.AppendAsync(key, rows).ConfigureAwait(true);
        await initial.Engine.FlushAsync().ConfigureAwait(true);
        await ((IAsyncDisposable)initial.Engine).DisposeAsync().ConfigureAwait(true);

        var segment = Directory.GetFiles(
            Path.Combine(_root, nameof(Bar)),
            "*.bsct",
            SearchOption.TopDirectoryOnly).Single();
        FlipOneByte(segment);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var reopen = () => ColumnarBuilder()
                .WithCorruptSegmentRecovery(CorruptSegmentRecoveryDisposition.FailClosed)
                .BuildStackAsync();

            await reopen.Should().ThrowAsync<InvalidDataException>(
                "each failed open must release its caller-owned WAL before the equivalent retry")
                .ConfigureAwait(true);
        }
    }

    [Fact]
    public void FailedBuildCleanup_DoesNotMaskOriginal_WhenDiagnosticAttachmentThrows()
    {
        var original = new DataRejectingException();

        var cleanup = () => WalOwningLsmStorageEngine<PriceBarDatasetKey, Bar>
            .DisposeAfterBuildFailure(owner: null, new ThrowingDisposable(), original);

        cleanup.Should().NotThrow(
            "a cleanup failure and a hostile Exception.Data implementation must not replace the original failure");
    }

    private TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar> ColumnarBuilder()
        => new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>(nameof(Bar))
            .WithSegmentRoot(Path.Combine(_root, "{record-type}"));

    private Task<TimeSeriesStack<DividendPaymentDatasetKey, DividendPayment>> BuildRowAsync()
        => RowTimeSeriesStoreFactory.BuildStackAsync<DividendPaymentDatasetKey, DividendPayment>(
            nameof(DividendPayment),
            RowOptions());

    private RowTimeSeriesStoreOptions RowOptions()
        => new()
        {
            SegmentRoot = Path.Combine(_root, "{record-type}"),
        };

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddMarketDataKernel(
            timeSeriesDirectory: Path.Combine(_root, "provider"),
            configurePipeline: options => options.UseTimeSeriesStore = true);
        return services.BuildServiceProvider();
    }

    private static void FlipOneByte(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        stream.Position = stream.Length / 2;
        var value = stream.ReadByte();
        stream.Position--;
        stream.WriteByte((byte)(value ^ 0xFF));
    }

    private sealed class ThrowingDisposable : IDisposable
    {
        public void Dispose() => throw new IOException("cleanup failed");
    }

    private sealed class DataRejectingException : Exception
    {
        public override System.Collections.IDictionary Data
            => throw new NotSupportedException("diagnostic dictionary unavailable");
    }
}
