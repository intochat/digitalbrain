using DigitalBrain.Neurons;
using DigitalBrain.Units;
using System.Collections.Concurrent;
using DigitalBrain.Capabilities;
using DigitalBrain.Kernel;
using DigitalBrain.Time;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Slots;

[Capability("sql-read", 1)]
public interface ISqlRead
{
    Task<T> Scalar<T>(string sql, object? args, CancellationToken ct);
}

public sealed class FakeSqlRead(int value) : ISqlRead
{
    public Task<T> Scalar<T>(string sql, object? args, CancellationToken ct) => Task.FromResult((T)(object)value);
}

[Signal("test.slots.paying")]
public sealed record SlotPaying : Signal;

[State("slot.stats")]
public sealed record SlotStats(int Value)
{
    public SlotStats() : this(0) { }
}

[State("slot.audit")]
public sealed record SlotAudit(int Value)
{
    public SlotAudit() : this(0) { }
}



public static class SlotApps
{
    [Synapse(typeof(Tick), "slot.stats", Every = "every 30m")]
    public static async Task<SlotStats> Refresh(Tick t, SlotStats state, [Slot(Name = "Db")] ISqlRead db, CancellationToken ct) =>
        state with { Value = await db.Scalar<int>("select 1", null, ct) };

    [Synapse(typeof(Tick), "slot.audit", Every = "every 30m")]
    public static async Task<SlotAudit> Refresh(Tick t, SlotAudit state, [Slot(Name = "Db")] ISqlRead db, CancellationToken ct) =>
        state with { Value = await db.Scalar<int>("select 1", null, ct) };


}



// Slots resolve through the TestOS capability fakes: a binding is the fake's name.
public sealed class SlotResolutionFacts
{
    private static readonly NeuronId Installs = NeuronId.Parse("sr/synapse.installs/default");
    private static readonly NeuronId Stats = NeuronId.Parse("sr/slot.stats/default");
    private static readonly NeuronId Audit = NeuronId.Parse("sr/slot.audit/default");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OS Start()
    {
        var os = OS.Local(_ => { }).Register(typeof(SlotResolutionFacts).Assembly);
        os.Clock.Advance(new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero) - os.Clock.GetUtcNow());
        os.Confirmations.ApproveAll();
        return os;
    }

    private static Task<InstallOutcome> Ask(OS os, string alias, string binding)
        => os.Get<IInstalls>(Installs).Install(new InstallRequest(alias, new Dictionary<string, string> { ["Db"] = binding }), Ct);

    private static async Task Install(OS os, string alias, string binding)
    {
        var outcome = await Ask(os, alias, binding);
        Assert.Equal(InstallStatus.Pending, outcome.Status);
        await os.Idle();
        Assert.NotNull(await os.Get<IInstalls>(Installs).Describe(alias, Ct));
    }

    private static async Task<int> Value(OS os, NeuronId id)
    {
        var state = await ((IBrainObserver)os).State(id, Ct);
        return state is { } json && json.TryGetProperty("value", out var value) ? value.GetInt32() : 0;
    }

    private static async Task Tick(OS os)
    {
        os.Clock.Advance(TimeSpan.FromMinutes(30));
        await os.Idle();
    }

    [Fact]
    public async Task AReactionUsesTheBoundFake()
    {
        await using var os = Start().With<ISqlRead>("analytics", new FakeSqlRead(7));
        await Install(os, "slot.stats", "analytics");

        await Tick(os);

        Assert.Equal(7, await Value(os, Stats));
    }

    [Fact]
    public async Task AnUnboundSlotIsRefusedAtInstall()
    {
        await using var os = Start().With<ISqlRead>("analytics", new FakeSqlRead(7));

        var outcome = await Ask(os, "slot.stats", "missing");
        var empty = await os.Get<IInstalls>(Installs).Install(new InstallRequest("slot.stats", new Dictionary<string, string>()), Ct);

        Assert.Equal(InstallStatus.Refused, outcome.Status);
        Assert.Contains("missing", outcome.Reason, StringComparison.Ordinal);
        Assert.Equal(InstallStatus.Refused, empty.Status);
        Assert.Empty(await os.Get<IInstalls>(Installs).Pending(Ct));
    }

    [Fact]
    public async Task TwoInstallsResolveTheirOwnBindings()
    {
        await using var os = Start().With<ISqlRead>("analytics", new FakeSqlRead(7)).With<ISqlRead>("finance", new FakeSqlRead(11));
        await Install(os, "slot.stats", "analytics");
        await Install(os, "slot.audit", "finance");

        await Tick(os);

        Assert.Equal(7, await Value(os, Stats));
        Assert.Equal(11, await Value(os, Audit));
    }

    [Fact]
    public async Task ARebindingIsSavedSilentlyAndReturnsUpdated()
    {
        await using var os = Start().With<ISqlRead>("analytics", new FakeSqlRead(7)).With<ISqlRead>("finance", new FakeSqlRead(11));
        await Install(os, "slot.stats", "analytics");

        var again = await Ask(os, "slot.stats", "finance");
        await os.Idle();

        Assert.Equal(InstallStatus.Updated, again.Status);
        Assert.Single(os.Confirmations.Requested);
        var record = await os.Get<IInstalls>(Installs).Describe("slot.stats", Ct);
        Assert.NotNull(record);
        Assert.Equal("finance", record.Bindings["Db"]);
        await Tick(os);
        Assert.Equal(11, await Value(os, Stats));
    }

    [Fact]
    public async Task ADisconnectedBindingFailsClosedAtRunTime()
    {
        await using var os = Start().With<ISqlRead>("analytics", new FakeSqlRead(7));
        var finished = new ConcurrentQueue<SynapseFinished>();
        await using var listener = await os.ClusterListeners.Listen<SynapseFinished>("slots", (_, signal, _) =>
        {
            finished.Enqueue(signal);
            return Task.CompletedTask;
        });
        await Install(os, "slot.stats", "analytics");

        os.Without<ISqlRead>("analytics");
        await Tick(os);
        await os.Idle();

        Assert.Contains(finished, f => f.Outcome == "failed" && f.Function.EndsWith(".Refresh", StringComparison.Ordinal) && f.Neuron == Stats.ToString());
        Assert.Single(os.Journal(Stats.Brain).Signals(Stats), item => SignalAliases.Of(item.Signal) == "synapse.failed");
        Assert.DoesNotContain(os.Journal(Stats.Brain).Signals(Stats), s => s.Signal is StateChanged);
    }
}
