using DigitalBrain.Units;
using System.Collections.Concurrent;
using DigitalBrain.Testing;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Framework.Tests.E2E.MorningStats;

public sealed class MorningStatsFacts
{
    private static readonly NeuronId Installs = NeuronId.Parse("ms/synapse.installs/default");
    private static readonly NeuronId Stats = NeuronId.Parse("ms/stats/default");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class CountingSql : ISqlRead
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<T> Scalar<T>(string sql, object? args, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult((T)(typeof(T) == typeof(int) ? 12 : (object)250.5m));
        }
    }

    private static OS Start(CountingSql sql)
    {
        var os = OS.Local(_ => { }).Register(typeof(MorningStatsFacts).Assembly).With<ISqlRead>("analytics", sql);
        os.Clock.Advance(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero) - os.Clock.GetUtcNow());
        return os;
    }

    private static Task<InstallOutcome> Ask(OS os)
        => os.Get<IInstalls>(Installs).Install(new InstallRequest("stats", new Dictionary<string, string> { ["Db"] = "analytics" }), Ct);

    [Fact]
    public async Task TheInstallPromptSaysExactlyWhatTheFileNeeds()
    {
        await using var os = Start(new CountingSql());

        var outcome = await Ask(os);

        Assert.Equal(InstallStatus.Pending, outcome.Status);
        var plan = Assert.Single(os.Confirmations.Requested).Plan;
        Assert.Equal("stats wants to: read-only SQL through Db; run daily at 07:00 UTC; react to 1 signal; show a view.", plan.Body);
    }

    [Fact]
    public async Task EachMorningTheChartUpdates()
    {
        var sql = new CountingSql();
        await using var os = Start(sql);
        os.Confirmations.ApproveAll();
        var views = new ConcurrentQueue<ViewChanged>();
        await using var listener = await os.ClusterListeners.Listen<ViewChanged>("morning", (_, signal, _) =>
        {
            views.Enqueue(signal);
            return Task.CompletedTask;
        });
        Assert.Equal(InstallStatus.Pending, (await Ask(os)).Status);
        await os.Idle();

        os.Clock.Advance(TimeSpan.FromHours(1));
        await os.Idle();
        os.Clock.Advance(TimeSpan.FromHours(24));
        await os.Idle();

        var reports = os.Journal(Stats.Brain).Signals(Stats).Select(s => s.Signal).OfType<ReportReady>().ToList();
        Assert.Equal([new DateOnly(2025, 12, 31), new DateOnly(2026, 1, 1)], reports.Select(r => r.Day));
        Assert.All(reports, r => Assert.Equal((12, 250.5m), (r.Orders, r.Revenue)));
        Assert.Equal(2, views.Count);
        Assert.Equal(4, sql.Calls);
    }

    [Fact]
    public async Task AReinstallWithTheSameFileDoesNotAsk()
    {
        await using var os = Start(new CountingSql());
        os.Confirmations.ApproveAll();
        Assert.Equal(InstallStatus.Pending, (await Ask(os)).Status);
        await os.Idle();

        var again = await Ask(os);

        Assert.Equal(InstallStatus.Unchanged, again.Status);
        Assert.Single(os.Confirmations.Requested);
    }
}
