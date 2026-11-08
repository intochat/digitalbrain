using DigitalBrain.Rules;
using DigitalBrain.Testing.Tests.Unit.Slots;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Host;

// The bindings of a unit travel with its source: deploy, restore and re-deploy read the same ones. The unit's gate reads its Db slot and
// rejects with the value it read, so the message tells which connection the slot resolved to.
public sealed class SourceBindingsFacts
{
    private static readonly string ReadsDb = HsSource.WithDb
        .Replace("t.Spent + s.Amount > 300m", "t.Spent + s.Amount > 0m", StringComparison.Ordinal)
        .Replace("public static Check WeeklyCap", "public static async Task<Check> WeeklyCap", StringComparison.Ordinal)
        .Replace("Check.Reject(\"weekly cap of $300\")", "Check.Reject($\"db {await Db.Scalar<int>(\"select\", null, CancellationToken.None)}\")", StringComparison.Ordinal);

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

    // Drives the unit through its Db slot: the gate rejects with what the bound connection returned.
    private static async Task<string> Read(OS os, string brain)
    {
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => os.Get<IHsPayments>(NeuronId.Parse($"{brain}/hs.payments/p")).Charge(1m));
        return e.Rejected.Messages[0];
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
        Assert.Equal("conn-2", (await os.Store.Get(brain, HsSource.Key, Ct))!.Bindings!["hs.week"]["Db"]);
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
        var record = (await os.Store.Get(brain, HsSource.Key, Ct))!;
        await os.Store.Put(brain, record with { Bindings = null }, Ct);
        Assert.Null((await os.Store.Get(brain, HsSource.Key, Ct))!.Bindings);

        await RestartAndTouch(os, "sb4");

        var pay = os.Get<IHsPayments>(NeuronId.Parse("sb4/hs.payments/p"));
        await pay.Charge(250m);
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => pay.Charge(100m));
        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
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
        Assert.Equal("conn-2", (await os.Store.Get(brain, HsSource.Key, Ct))!.Bindings!["hs.week"]["Db"]);
        Assert.Equal(DeployStatus.Unchanged, (await os.Synapses.Deploy(brain, Source(Bind("conn-2")), None, Ct)).Status);
    }
}
