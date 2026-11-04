using DigitalBrain.Spi;
using DigitalBrain.Time;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

[Signal("test.ms.report-ready")]
public sealed record MsReportReady(DateOnly Day, int Orders, decimal Revenue, DateTimeOffset At) : Signal;

[State("stats1m")]
public sealed record MsStats(DateOnly? Day = null, int Orders = 0, decimal Revenue = 0)
{
    public MsStats() : this(null, 0, 0) { }
}

// The morning-stats file of the E2E project with a schedule the silo's real clock can reach: the silo clock cannot be advanced,
// so the daily 07:00 becomes "every 1m". The shared FakeSqlRead answers 7 to every scalar.
public static class StatsEveryMinute
{
    [Synapse(from: typeof(Tick), into: "stats1m", Every = "every 1m")]
    public static async Task<Reaction<MsStats>> Refresh(Tick t, MsStats s, [Slot] ISqlRead Db, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(t.At.UtcDateTime.Date).AddDays(-1);
        var orders = await Db.Scalar<int>("select count(*)", null, ct);
        var revenue = await Db.Scalar<int>("select sum(total)", null, ct);
        return Reaction<MsStats>.Of(new MsStats(day, orders, revenue), new MsReportReady(day, orders, revenue, t.At));
    }
}

public sealed class MorningStatsSiloFacts(SlotSilo silo) : IClassFixture<SlotSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IInstalls Installs(string brain) => silo.Get<IInstalls>($"{brain}/synapse.installs/default");

    private Task<InstallOutcome> Ask(string brain)
        => Installs(brain).Install(new InstallRequest("stats1m", new Dictionary<string, string> { ["Db"] = "analytics" }), Ct);

    private async Task Install(string brain)
    {
        var outcome = await Ask(brain);
        Assert.True(outcome.Status is InstallStatus.Pending, outcome.Reason);
        await StateNeuronSiloFacts.Until(async () => await Installs(brain).Describe("stats1m", Ct) is not null, Ct);
        Assert.NotNull(await Installs(brain).Describe("stats1m", Ct));
    }

    private async Task<IReadOnlyList<Signal>> Journal(string brain, NeuronId id)
        => [.. (await silo.Services.GetRequiredService<BrainSignalHistories>().For(id.Brain).Read(id, 0, 100, Ct)).Signals.Select(s => s.Signal)];

    private static async Task WaitFor(string what, Func<Task<bool>> condition, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (!await condition())
        {
            if (DateTime.UtcNow >= deadline) { throw new TimeoutException($"Waited {limit} for {what}."); }
            await Task.Delay(500, Ct);
        }
    }

    [Fact]
    public async Task AReinstallWithTheSameFileDoesNotAsk()
    {
        await Install("msa");

        var again = await Ask("msa");

        Assert.Equal(InstallStatus.Unchanged, again.Status);
        Assert.Empty(await Installs("msa").Pending(Ct));
    }

    // Real time: the first tick arrives within about a minute, the second a minute later. The grain is deactivated between them.
    [Fact]
    [Trait("Category", "Slow")]
    public async Task EachMinuteTheStatsUpdateAndSurviveADeactivation()
    {
        var stats = NeuronId.Parse("msb/stats1m/default");
        await Install("msb");

        await WaitFor("the first MsReportReady of msb", async () => (await Journal("msb", stats)).OfType<MsReportReady>().Any(), TimeSpan.FromSeconds(100));

        // Best-effort: the collection is global and the fixture offers nothing to observe that this one grain was deactivated.
        // The exactly-once guarantee is pinned by the count and distinct-tick assertions below regardless.
        await silo.Services.GetRequiredService<IGrainFactory>().GetGrain<IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero, Ct);

        await WaitFor("the second MsReportReady of msb", async () => (await Journal("msb", stats)).OfType<MsReportReady>().Count() >= 2, TimeSpan.FromSeconds(90));

        // Settle, then re-read: a late duplicate delivery would show up as a third entry.
        await Task.Delay(TimeSpan.FromSeconds(3), Ct);
        var journal = await Journal("msb", stats);
        Assert.Equal(2, journal.OfType<MsReportReady>().Count());
        // Both ticks fall on the same UTC day, so the tick instant is what tells the two reports apart.
        Assert.Equal(2, journal.OfType<MsReportReady>().Select(r => r.At).Distinct().Count());
        // A reaction that returns its own signals saves them in place of StateChanged, so these two entries are the only saves.
        Assert.Equal(2, journal.Count);
        Assert.All(journal.OfType<MsReportReady>(), r => Assert.Equal((7, 7m), (r.Orders, r.Revenue)));
    }
}
