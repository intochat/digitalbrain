using DigitalBrain.Testing.Tests.Unit.Slots;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Host;

// The bindings of a unit travel with its source: deploy, restore and re-deploy read the same ones.
public sealed class SourceBindingsFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Bind(string connection) =>
        new Dictionary<string, IReadOnlyDictionary<string, string>> { ["hs.week"] = new Dictionary<string, string> { ["Db"] = connection } };

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> None => new Dictionary<string, IReadOnlyDictionary<string, string>>();

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    private static SynapseSource Source(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? bindings) =>
        HsSource.Of(HsSource.WithDb) with { Bindings = bindings };

    private static OS Start()
    {
        var os = OS.Local().With<ISqlRead>("conn-1", new FakeSqlRead(1)).With<ISqlRead>("conn-2", new FakeSqlRead(2));
        return os;
    }

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

    private static Task<InstallRecord?> Installed(OS os, BrainId brain) =>
        os.Get<IInstalls>(new NeuronId(brain, "synapse.installs", "default")).Describe("hs.week", Ct);

    [Fact]
    public async Task DeployBindsSlotsFromTheSource()
    {
        var os = Start();
        var brain = Brain("sb1");

        _ = await Deploy(os, brain, Source(Bind("conn-1")), None);

        Assert.Equal("conn-1", (await Installed(os, brain))!.Bindings["Db"]);
    }

    [Fact]
    public async Task RestoreKeepsTheBindings()
    {
        var os = Start();
        var brain = Brain("sb2");
        _ = await Deploy(os, brain, Source(Bind("conn-1")), None);

        await os.Restart();
        Assert.True(await os.Restorer.TryRestore(brain, "hs.week", Ct));

        Assert.Equal("conn-1", (await Installed(os, brain))!.Bindings["Db"]);
        var stored = await os.Store.Get(brain, HsSource.Key, Ct);
        Assert.Equal("conn-1", stored!.Bindings!["hs.week"]["Db"]);
    }

    [Fact]
    public async Task ExplicitArgumentWinsOverSource()
    {
        var os = Start();
        var brain = Brain("sb3");

        _ = await Deploy(os, brain, Source(Bind("conn-1")), Bind("conn-2"));

        Assert.Equal("conn-2", (await Installed(os, brain))!.Bindings["Db"]);
    }
}
