using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using Microsoft.Extensions.DependencyInjection;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Probe;

// 04-14, 06 D-15: host-grain dispatch versus a typed grain call, measured with the Phase 1 spike's interleaved method.
public sealed class HostCostFacts
{
    private const int Warmup = 10_000;
    private const int Measured = 100_000;
    private const int Round = 1_000;

    private sealed record Payload(string Name, Func<ITypedProbe, Task> Typed, Func<IProbe, Task> Host);

    private sealed class Path
    {
        public readonly long[] Ticks = new long[Measured];
        public int Count;
        public long Bytes;
    }

    [Fact(Explicit = true)]
    public async Task HostDispatchCost()
    {
        var ct = TestContext.Current.CancellationToken;
        var silo = new StatefulSilo();
        await silo.InitializeAsync();
        try
        {
            var typed = silo.Services.GetRequiredService<OrleansSdk.IGrainFactory>().GetGrain<ITypedProbe>("cost");
            var host = silo.Get<IProbe>("b1/probe.shapes/cost");
            string Text(int bytes) => new('x', bytes);
            var b16 = Text(16);
            var k1 = Text(1024);
            var k32 = Text(32 * 1024);
            Payload[] payloads =
            [
                new("int", t => t.Add(1, 2), h => h.Add(1, 2)),
                new("16 B", t => t.Echo(b16).AsTask(), h => h.Echo(b16).AsTask()),
                new("1 KiB", t => t.Echo(k1).AsTask(), h => h.Echo(k1).AsTask()),
                new("32 KiB", t => t.Echo(k32).AsTask(), h => h.Echo(k32).AsTask()),
            ];

            var report = new StringBuilder();
            report.AppendLine(CultureInfo.InvariantCulture, $"machine: {Environment.MachineName}, {Environment.ProcessorCount} logical cores, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");
            report.AppendLine(CultureInfo.InvariantCulture, $"runtime: {RuntimeInformation.FrameworkDescription}, Orleans {typeof(OrleansSdk.Grain).Assembly.GetName().Version}, server GC {System.Runtime.GCSettings.IsServerGC}");
            report.AppendLine(CultureInfo.InvariantCulture, $"warmup {Warmup}, measured {Measured} per path and payload, interleaved rounds of {Round}");
            report.AppendLine("| payload | path | p50 us | p99 us | B/call | p50 ratio | extra B/call |");
            report.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var payload in payloads)
            {
                for (var i = 0; i < Warmup; i++)
                {
                    await payload.Typed(typed);
                    await payload.Host(host);
                }
                var paths = new[] { new Path(), new Path() };
                for (var round = 0; round < Measured / Round; round++)
                {
                    ct.ThrowIfCancellationRequested();
                    // Rotate which path goes first so neither benefits from run order.
                    for (var turn = 0; turn < 2; turn++)
                    {
                        var which = (round + turn) % 2;
                        var path = paths[which];
                        var before = GC.GetTotalAllocatedBytes(precise: true);
                        for (var i = 0; i < Round; i++)
                        {
                            var start = Stopwatch.GetTimestamp();
                            await (which == 0 ? payload.Typed(typed) : payload.Host(host));
                            path.Ticks[path.Count++] = Stopwatch.GetTimestamp() - start;
                        }
                        path.Bytes += GC.GetTotalAllocatedBytes(precise: true) - before;
                    }
                }
                var (t50, t99, tb) = Stats(paths[0]);
                var (h50, h99, hb) = Stats(paths[1]);
                report.AppendLine(CultureInfo.InvariantCulture, $"| {payload.Name} | typed | {t50:F2} | {t99:F2} | {tb:F0} | 1.00 | 0 |");
                report.AppendLine(CultureInfo.InvariantCulture, $"| {payload.Name} | host | {h50:F2} | {h99:F2} | {hb:F0} | {h50 / t50:F2} | {hb - tb:F0} |");
            }

            var text = report.ToString();
            TestContext.Current.TestOutputHelper?.WriteLine(text);
            await File.WriteAllTextAsync(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "digitalbrain-host-cost.md"), text, ct);
        }
        finally { await silo.DisposeAsync(); }
    }

    private static (double P50, double P99, double Bytes) Stats(Path path)
    {
        Array.Sort(path.Ticks);
        double Us(long ticks) => ticks * 1_000_000.0 / Stopwatch.Frequency;
        return (Us(path.Ticks[Measured / 2]), Us(path.Ticks[(int)(Measured * 0.99)]), (double)path.Bytes / Measured);
    }
}
