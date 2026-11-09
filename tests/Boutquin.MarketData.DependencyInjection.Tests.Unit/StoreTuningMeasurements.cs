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

#pragma warning disable
using System.Diagnostics;
using System.Text;

using Boutquin.MarketData.Abstractions.Keys;
using Boutquin.MarketData.Abstractions.Observability;
using Boutquin.MarketData.Abstractions.Records;
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Cache.Keys;
using Boutquin.MarketData.Storage.Snapshots;
using Boutquin.Storage.Domain.Enums;

using Xunit;

namespace Boutquin.MarketData.DependencyInjection.Tests.Unit;

// Opt-in measurement harness; every case returns immediately unless named in MD_PERF.
// Cases: wal,bloom,cov,mem,key,legacy. Results append to MD_PERF_OUT (default: temp md-perf.txt).
// Run: MD_PERF=wal /usr/local/share/dotnet/dotnet test -c Release --filter StoreTuningMeasurements
public sealed class StoreTuningMeasurements
{
    private static readonly string Out = Environment.GetEnvironmentVariable("MD_PERF_OUT") ?? Path.Combine(Path.GetTempPath(), "md-perf.txt");
    private static readonly DateOnly s_first = new(1990, 1, 2);
    private static Bar BarAt(int day) => new(s_first.AddDays(day), 100, 101, 99, 100, 100, 1000);
    private static PriceBarDatasetKey Ds(int i) => new("fixture", DataFrequency.Daily, "S" + i, null);

    private static void Log(string line) { File.AppendAllText(Out, line + "\n"); }
    private static bool On(string name) => (Environment.GetEnvironmentVariable("MD_PERF") ?? "").Split(',').Contains(name);
    private static double Median(IEnumerable<double> v) { var a = v.OrderBy(x => x).ToArray(); return a[a.Length / 2]; }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "md-perf-" + Guid.NewGuid().ToString("N"));

    private static TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar> B(string root, bool row, int batch, int lat)
    {
        var b = new TimeSeriesStoreBuilder<PriceBarDatasetKey, Bar>("Bar").WithSegmentRoot(root)
            .WithWriteAheadLogOptions(w => { w.GroupCommitBatchSize = batch; w.GroupCommitLatencyMs = lat; });
        return row ? b.UseRowFormat() : b.UseColumnarFormat();
    }

    private static long DirBytes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(f => !f.Contains("/wal/", StringComparison.Ordinal)).Sum(f => new FileInfo(f).Length);

    [Fact]
    public async Task WalGroupCommit()
    {
        if (!On("wal")) { return; }
        foreach (var row in new[] { false, true })
        {
            foreach (var (batch, lat) in new[] { (256, 5), (256, 1), (64, 1), (1, 1) })
            {
                foreach (var (name, calls, per) in new[] { ("backfill-1x2520", 1, 2520), ("refresh-100x25", 100, 25), ("single-300x1", 300, 1) })
                {
                    var times = new List<double>();
                    for (var rep = 0; rep < 3; rep++)
                    {
                        var root = NewRoot();
                        var stack = await B(root, row, batch, lat).BuildStackAsync();
                        await using (var o = (IAsyncDisposable)stack.Engine)
                        {
                            var sw = Stopwatch.StartNew();
                            for (var c = 0; c < calls; c++)
                            {
                                await stack.Store.AppendAsync(Ds(c % 20), Enumerable.Range(c * per, per).Select(BarAt).ToArray());
                            }
                            times.Add(sw.Elapsed.TotalMilliseconds);
                        }
                        Directory.Delete(root, true);
                    }
                    Log($"WAL {(row ? "row" : "col")} batch={batch} lat={lat} {name}: median {Median(times):F1} ms (all {string.Join('/', times.Select(t => t.ToString("F0")))})");
                }
            }
        }
    }

    [Fact]
    public async Task Bloom()
    {
        if (!On("bloom")) { return; }
        foreach (var row in new[] { false, true })
        {
            foreach (var expected in new int?[] { 131_072, null })
            {
                var times = new List<double>();
                long bytes = 0;
                for (var rep = 0; rep < 3; rep++)
                {
                    var root = NewRoot();
                    var b = B(root, row, 256, 5).WithBloomFilter(new TimeSeriesBloomFilterOptions { ExpectedItems = expected });
                    var stack = await b.BuildStackAsync();
                    await using (var o = (IAsyncDisposable)stack.Engine)
                    {
                        var sw = Stopwatch.StartNew();
                        for (var f = 0; f < 40; f++)
                        {
                            await stack.Store.AppendAsync(Ds(f), Enumerable.Range(0, 25).Select(BarAt).ToArray());
                            await stack.Engine.FlushAsync();
                        }
                        times.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    bytes = DirBytes(root);
                    Directory.Delete(root, true);
                }
                Log($"BLOOM {(row ? "row" : "col")} expected={(expected?.ToString() ?? "actual")} 40 small flushes: median {Median(times):F1} ms, disk {bytes:N0} bytes");
            }
        }
    }

    [Fact]
    public async Task Coverage()
    {
        if (!On("cov")) { return; }
        foreach (var persisted in new[] { false, true })
        {
            foreach (var datasets in new[] { 10, 500, 3000 })
            {
                var root = NewRoot();
                var b = B(root, true, 1, 1);
                if (persisted) { b.UsePersistedCoverageIndex(); }
                var stack = await b.BuildStackAsync();
                await using (var o = (IAsyncDisposable)stack.Engine)
                {
                    var sw = Stopwatch.StartNew();
                    for (var d = 0; d < datasets; d++)
                    {
                        await stack.Store.AppendAsync(Ds(d), Enumerable.Range(0, 25).Select(BarAt).ToArray());
                    }
                    var ms = sw.Elapsed.TotalMilliseconds;
                    Log($"COV write persisted={persisted} datasets={datasets}: {ms:F0} ms total, {ms / datasets:F2} ms/append");
                }
                Directory.Delete(root, true);
            }

            foreach (var rows in new[] { 20_000, 200_000 })
            {
                var root = NewRoot();
                var b = B(root, false, 256, 5);
                if (persisted) { b.UsePersistedCoverageIndex(); }
                var stack = await b.BuildStackAsync();
                await using (var o = (IAsyncDisposable)stack.Engine)
                {
                    await stack.Store.AppendAsync(Ds(0), Enumerable.Range(0, rows).Select(BarAt).ToArray());
                    await stack.Engine.FlushAsync();
                }
                var b2 = B(root, false, 256, 5);
                if (persisted) { b2.UsePersistedCoverageIndex(); }
                var reopened = await b2.BuildStackAsync();
                await using (var o = (IAsyncDisposable)reopened.Engine)
                {
                    var sw = Stopwatch.StartNew();
                    var cov = await reopened.Store.GetCoverageAsync(Ds(0));
                    Log($"COV cold-first-query persisted={persisted} rows={rows}: {sw.Elapsed.TotalMilliseconds:F1} ms (coverage {cov})");
                }
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public async Task MemTable()
    {
        if (!On("mem")) { return; }
        foreach (var row in new[] { false, true })
        {
            foreach (var cap in new[] { 1_000, 10_000, 100_000 })
            {
                var root = NewRoot();
                var stack = await B(root, row, 256, 5).WithMemTableCapacity(cap).BuildStackAsync();
                await using (var o = (IAsyncDisposable)stack.Engine)
                {
                    var sw = Stopwatch.StartNew();
                    for (var c = 0; c < 100; c++)
                    {
                        await stack.Store.AppendAsync(Ds(c % 5), Enumerable.Range(c * 1000, 1000).Select(BarAt).ToArray());
                    }
                    await stack.Engine.FlushAsync();
                    var ingest = sw.Elapsed.TotalMilliseconds;
                    var segs = Directory.EnumerateFiles(root, "ts*", SearchOption.TopDirectoryOnly).Count();
                    sw.Restart();
                    var n = 0;
                    for (var q = 0; q < 50; q++)
                    {
                        n += (await stack.Store.GetRangeAsync(Ds(q % 5), s_first.AddDays(q * 200), s_first.AddDays(q * 200 + 3000)).ToListAsync()).Count;
                    }
                    Log($"MEM {(row ? "row" : "col")} cap={cap}: ingest {ingest:F0} ms, segments {segs}, 50 range reads {sw.Elapsed.TotalMilliseconds:F0} ms ({n} rows)");
                }
                Directory.Delete(root, true);
            }
        }
    }

    [Fact]
    public void KeyEncoding()
    {
        if (!On("key")) { return; }
        var request = new PriceHistoryRequest(
            Enumerable.Range(0, 10).Select(i => new Boutquin.MarketData.Abstractions.ReferenceData.Symbol("TICK" + i)).ToArray(),
            new Boutquin.Domain.ValueObjects.DateRange(new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31)))
        { Basis = PriceBasis.Raw };
        for (var warm = 0; warm < 2; warm++)
        {
            foreach (var (name, fn) in new (string, Func<string>)[]
            {
                ("CacheKeyBuilder.FromDateRange(len 40)", () => CacheKeyBuilder.FromDateRange("price-history", "SPY,QQQ,IWM,EFA,EEM,AGG,TLT,GLD,VNQ,DBC", new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31))),
                ("PriceHistoryRequest.DatasetKey (10 symbols)", () => request.DatasetKey),
            })
            {
                const int N = 100_000;
                var a0 = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                var len = 0;
                for (var i = 0; i < N; i++) { len += fn().Length; }
                var ns = sw.Elapsed.TotalMilliseconds * 1e6 / N;
                var alloc = (GC.GetAllocatedBytesForCurrentThread() - a0) / (double)N;
                if (warm == 1) { Log($"KEY {name}: {ns:F0} ns/op, {alloc:F0} B/op"); }
            }
        }
    }

    [Fact]
    public async Task LegacyScan()
    {
        if (!On("legacy")) { return; }
        foreach (var n in new[] { 100, 5_000, 20_000 })
        {
            var root = NewRoot();
            var store = new FileSnapshotStore(root);
            for (var i = 0; i < n; i++)
            {
                await store.WriteAsync("k/" + i, new MemoryStream(Encoding.UTF8.GetBytes("x")));
            }
            var times = new List<double>();
            for (var rep = 0; rep < 5; rep++)
            {
                var sw = Stopwatch.StartNew();
                for (var q = 0; q < 20; q++) { _ = await store.ExistsAsync("miss/" + q); }
                times.Add(sw.Elapsed.TotalMilliseconds / 20);
            }
            Log($"LEGACY ExistsAsync miss with {n} stored snapshots: median {Median(times):F2} ms/miss");
            Directory.Delete(root, true);
        }
    }
}
