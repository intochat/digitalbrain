using DigitalBrain.Units;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Tests.Unit.Slots;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Host;

// The bindings of a unit travel with its source: deploy, restore and re-deploy read the same ones. The unit's reaction reads its Db slot and
// sets the state to the value it read, so the state tells which connection the slot resolved to.
public sealed class SourceBindingsFacts
{
    private static readonly string ReadsDb = HsSource.WithDb
        .Replace("public static WeekSpend WeeklyCap", "public static async Task<WeekSpend> WeeklyCap", StringComparison.Ordinal)
        .Replace("Spent = t.Spent + s.Amount", "Spent = await Db.Scalar<int>(\"select\", null, CancellationToken.None)", StringComparison.Ordinal);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Bind(string connection) =>
        new() { ["hs.week"] = new Dictionary<string, string> { ["Db"] = connection } };

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> None => new Dictionary<string, IReadOnlyDictionary<string, string>>();

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    private static SynapseSource Source(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? bindings) =>
        HsSource.Of(ReadsDb) with { Bindings = bindings };

    private static OS Start() =>
        OS.Local().Register<HsPayments>().With<ISqlRead>("conn-1", new FakeSqlRead(1)).With<ISqlRead>("conn-2", new FakeSqlRead(2));

    // Deploys and approves the install the person is asked for.
    private static async Task<DeployOutcome> Deploy(OS os, BrainId brain, SynapseSource source, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> argument)
    {
        var outcome = await os.Synapses.Deploy(brain, source, argument, Ct);
        Assert.Equal(DeployStatus.Deployed, outcome.Status);
        Assert.Equal(InstallStatus.Pending, outcome.Installs["hs.week"].Status);
        await os.Confirmations.Approve(outcome.Installs["hs.week"].Reason!);
        await os.Idle();
        return outcome;
    }

    // The first activation of the state alias after the restart restores the source from the brain's record.
    private static async Task RestartAndTouch(OS os, string brain)
    {
        await os.Restart();
        await os.Activate(NeuronId.Parse($"{brain}/hs.week/default"));
    }

    // Drives the unit through its Db slot: the reaction stores what the bound connection returned.
    private static async Task<string> Read(OS os, string brain)
    {
        await os.Get<IHsPayments>(NeuronId.Parse($"{brain}/hs.payments/p")).Charge(1m);
        await os.Idle();
        var state = await ((IBrainObserver)os).State(NeuronId.Parse($"{brain}/hs.week/default"), Ct);
        return $"db {state!.Value.GetProperty("spent").GetInt32()}";
    }

    [Fact]
    public async Task DeployBindsSlotsFromTheSource()
    {
        await using var os = Start();

        _ = await Deploy(os, Brain("sb1"), Source(Bind("conn-1")), None);

        Assert.Equal("db 1", await Read(os, "sb1"));
    }

    [Fact]
    public async Task RestoreKeepsTheBindings()
    {
        await using var os = Start();
        var brain = Brain("sb2");
        _ = await Deploy(os, brain, Source(Bind("conn-2")), None);

        await RestartAndTouch(os, "sb2");

        Assert.Equal("db 2", await Read(os, "sb2"));
        var installs2 = os.Get<IInstalls>(NeuronId.Parse("sb2/synapse.installs/default"));
        Assert.Equal("conn-2", (await installs2.Describe("hs.week", Ct))!.Bindings["Db"]);
    }

    [Fact]
    public async Task ExplicitArgumentWinsOverSource()
    {
        await using var os = Start();

        _ = await Deploy(os, Brain("sb3"), Source(Bind("conn-1")), Bind("conn-2"));

        Assert.Equal("db 2", await Read(os, "sb3"));
    }

    [Fact]
    public async Task AnOldRecordWithoutBindingsRestoresAsBefore()
    {
        await using var os = Start();
        var brain = Brain("sb4");
        _ = await Deploy(os, brain, HsSource.Of(HsSource.Budget), None);

        await RestartAndTouch(os, "sb4");

        var pay = os.Get<IHsPayments>(NeuronId.Parse("sb4/hs.payments/p"));
        await pay.Charge(250m);
        await os.Idle();
        var state = await ((IBrainObserver)os).State(NeuronId.Parse("sb4/hs.week/default"), Ct);
        Assert.Equal(250m, state!.Value.GetProperty("spent").GetDecimal());
    }

    [Fact]
    public async Task SameFilesDifferentBindingsReinstalls()
    {
        await using var os = Start();
        var brain = Brain("sb5");
        var first = await Deploy(os, brain, Source(Bind("conn-1")), None);
        Assert.Equal("db 1", await Read(os, "sb5"));
        Assert.Equal(DeployStatus.Unchanged, (await os.Synapses.Deploy(brain, Source(Bind("conn-1")), None, Ct)).Status);

        var again = await os.Synapses.Deploy(brain, Source(Bind("conn-2")), None, Ct);
        await os.Idle();

        Assert.Equal(DeployStatus.Deployed, again.Status);
        Assert.Equal(first.Hash, again.Hash);
        Assert.Equal(InstallStatus.Updated, again.Installs["hs.week"].Status);
        Assert.Equal("db 2", await Read(os, "sb5"));
        var installs5 = os.Get<IInstalls>(NeuronId.Parse("sb5/synapse.installs/default"));
        Assert.Equal("conn-2", (await installs5.Describe("hs.week", Ct))!.Bindings["Db"]);
        Assert.Equal(DeployStatus.Unchanged, (await os.Synapses.Deploy(brain, Source(Bind("conn-2")), None, Ct)).Status);
    }

    [Fact]
    public async Task SameFilesNoBindingsStaysUnchanged()
    {
        await using var os = Start();
        var brain = Brain("sb6");
        _ = await Deploy(os, brain, Source(Bind("conn-1")), None);

        var again = await os.Synapses.Deploy(brain, Source(null), None, Ct);

        Assert.Equal(DeployStatus.Unchanged, again.Status);
        var installs6 = os.Get<IInstalls>(NeuronId.Parse("sb6/synapse.installs/default"));
        Assert.Equal("conn-1", (await installs6.Describe("hs.week", Ct))!.Bindings["Db"]);
        Assert.Equal("db 1", await Read(os, "sb6"));
    }

    [Fact]
    public async Task ARefusedRebindLeavesEveryAliasOnItsOldBinding()
    {
        await using var os = Start();
        var brain = Brain("sb7");
        var two = HsSource.Of(TwoAliases);
        var both = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["hs.week"] = new Dictionary<string, string> { ["Db"] = "conn-1" },
            ["hs.week2"] = new Dictionary<string, string> { ["Db"] = "conn-1" },
        };
        var first = await os.Synapses.Deploy(brain, two with { Bindings = both }, None, Ct);
        foreach (var install in first.Installs.Values) { await os.Confirmations.Approve(install.Reason!); }
        await os.Idle();
        var bad = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["hs.week"] = new Dictionary<string, string> { ["Db"] = "conn-2" },
            ["hs.week2"] = new Dictionary<string, string> { ["Db"] = "nowhere" },
        };

        var again = await os.Synapses.Deploy(brain, two with { Bindings = bad }, None, Ct);

        Assert.Equal(DeployStatus.Refused, again.Status);
        var installs = os.Get<IInstalls>(NeuronId.Parse("sb7/synapse.installs/default"));
        Assert.Equal("conn-1", (await installs.Describe("hs.week", Ct))!.Bindings["Db"]);
        Assert.Equal("conn-1", (await installs.Describe("hs.week2", Ct))!.Bindings["Db"]);
        Assert.Equal("db 1", await Read(os, "sb7"));
    }

    [Fact]
    public async Task ARebindThatWaitsForThePersonIsNotStoredYet()
    {
        await using var os = Start();
        var brain = Brain("sb8");
        // The first install is never answered, so the same needs are asked again, not re-bound.
        var first = await os.Synapses.Deploy(brain, Source(Bind("conn-1")), None, Ct);
        Assert.Equal(InstallStatus.Pending, first.Installs["hs.week"].Status);

        var again = await os.Synapses.Deploy(brain, Source(Bind("conn-2")), None, Ct);

        Assert.Equal(DeployStatus.Deployed, again.Status);
        Assert.Equal(InstallStatus.Pending, again.Installs["hs.week"].Status);
        var installs8 = os.Get<IInstalls>(NeuronId.Parse("sb8/synapse.installs/default"));
        Assert.Null(await installs8.Describe("hs.week", Ct));

        await os.Confirmations.Approve(again.Installs["hs.week"].Reason!);
        await os.Idle();

        Assert.Equal("conn-2", (await installs8.Describe("hs.week", Ct))!.Bindings["Db"]);
    }

    [Fact]
    public async Task AnApprovedRebindIsStored()
    {
        await using var os = Start();
        var brain = Brain("sb9");
        var two = HsSource.Of(TwoAliases);
        var first = await os.Synapses.Deploy(brain, two with { Bindings = Both("conn-1", "conn-1") }, None, Ct);
        // Only hs.week is approved: hs.week2 stays asked, so the redeploy re-binds one alias now and asks again for the other.
        await os.Confirmations.Approve(first.Installs["hs.week"].Reason!);
        await os.Idle();

        var again = await os.Synapses.Deploy(brain, two with { Bindings = Both("conn-2", "conn-2") }, None, Ct);
        await os.Idle();

        Assert.Equal(InstallStatus.Updated, again.Installs["hs.week"].Status);
        Assert.Equal(InstallStatus.Pending, again.Installs["hs.week2"].Status);
        var installs9 = os.Get<IInstalls>(NeuronId.Parse("sb9/synapse.installs/default"));
        Assert.Equal("conn-2", (await installs9.Describe("hs.week", Ct))!.Bindings["Db"]);
        Assert.Null(await installs9.Describe("hs.week2", Ct));

        await os.Confirmations.Approve(again.Installs["hs.week2"].Reason!);
        await os.Idle();

        Assert.Equal("conn-2", (await installs9.Describe("hs.week", Ct))!.Bindings["Db"]);
        Assert.Equal("conn-2", (await installs9.Describe("hs.week2", Ct))!.Bindings["Db"]);
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Both(string week, string week2) => new()
    {
        ["hs.week"] = new Dictionary<string, string> { ["Db"] = week },
        ["hs.week2"] = new Dictionary<string, string> { ["Db"] = week2 },
    };

    // The reaction source plus a second state alias that has its own Db slot.
    private static readonly string TwoAliases = ReadsDb + """

        [Signal("hs.charging2")]
        public sealed record Charging2(decimal Amount) : Signal;

        [State("hs.week2")]
        public sealed record WeekSpend2(decimal Spent = 0)
        {
            public WeekSpend2() : this(0) { }
        }

        public static class Budget2
        {
            [Synapse(typeof(Charging2), "hs.week2")]
            public static WeekSpend2 Cap2(Charging2 s, WeekSpend2 t, [Slot] ISqlRead Db) => t with { Spent = t.Spent + s.Amount };
        }
        """;
}
