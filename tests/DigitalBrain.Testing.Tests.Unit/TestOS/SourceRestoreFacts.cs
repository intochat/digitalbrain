using DigitalBrain.Rules;
using DigitalBrain.Testing.Tests.Unit.Slots;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Host;

public sealed class SourceRestoreFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> None => new Dictionary<string, IReadOnlyDictionary<string, string>>();

    private static OS Start() => OS.Local().Register<HsPayments>().Register<SrPayments>().With<ISqlRead>("analytics", new FakeSqlRead(1));

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    private static IHsPayments Pay(OS os, string brain) => os.Get<IHsPayments>(NeuronId.Parse($"{brain}/hs.payments/p"));

    private static IHsPayments Ask(OS os, string brain) => os.Get<IHsPayments>(NeuronId.Parse($"{brain}/sr.payments/p"));

    private static ISources Sources(OS os, string brain) => os.Get<ISources>(NeuronId.Parse($"{brain}/synapse.sources/default"));

    private static async Task<DeployOutcome> DeployApproved(OS os, string brain)
    {
        var outcome = await os.Synapses.Deploy(Brain(brain), HsSource.Of(HsSource.Budget), None, Ct);
        Assert.Equal(DeployStatus.Deployed, outcome.Status);
        await os.Confirmations.Approve(outcome.Installs["hs.week"].Reason!);
        await os.Idle();
        return outcome;
    }

    // The first activation of the state alias after the restart misses the registry and restores the source from the brain's record.
    private static async Task RestartAndTouch(OS os, string brain)
    {
        await os.Restart();
        await os.Activate(NeuronId.Parse($"{brain}/hs.week/default"));
    }

    [Fact]
    public async Task ADeployIsRecordedWithItsBytes()
    {
        await using var os = Start();
        var outcome = await DeployApproved(os, "sr1");

        var record = await Sources(os, "sr1").Get(HsSource.Key, Ct);

        Assert.NotNull(record);
        Assert.Equal(outcome.Hash, record.Hash);
        Assert.NotEmpty(record.Assembly);
        Assert.Equal(["hs.week"], record.Aliases);
        Assert.Single(await Sources(os, "sr1").List(Ct));
    }

    [Fact]
    public async Task AfterRestartTheFirstGateHitRestoresTheSource()
    {
        await using var os = Start();
        var outcome = await DeployApproved(os, "sr2");
        await Ask(os, "sr2").Charge(250m);
        await os.Idle();

        await os.Restart();
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => Ask(os, "sr2").Charge(100m));
        await os.Idle();

        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
        var listed = Assert.Single(await os.Synapses.List(Brain("sr2"), Ct));
        Assert.Equal(DeployStatus.Deployed, listed.Status);
        Assert.Equal(outcome.Hash, listed.Hash);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task TheRestoredSourceIsNotCompiledAgain()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr3");
        var emits = os.Synapses.Compiles;

        await RestartAndTouch(os, "sr3");

        Assert.Equal(emits, os.Synapses.Compiles);
        Assert.Contains("hs.week", os.Catalog.States.Keys);
    }

    [Fact]
    public async Task RetireRemovesTheRecord()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr4");

        Assert.Equal(DeployStatus.Retired, (await os.Synapses.Retire(Brain("sr4"), HsSource.Key, Ct)).Status);

        Assert.Null(await Sources(os, "sr4").Get(HsSource.Key, Ct));
        Assert.Empty(await Sources(os, "sr4").List(Ct));
    }

    [Fact]
    public async Task ARetiredAliasDoesNotKeepTheGatePathAsynchronous()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr6");
        await os.Restart();

        Assert.Equal(DeployStatus.Retired, (await os.Synapses.Retire(Brain("sr6"), HsSource.Key, Ct)).Status);
        await os.Idle();
        await Ask(os, "sr6").Charge(1m).ContinueWith(_ => { }, Ct, TaskContinuationOptions.None, TaskScheduler.Default);
        await Ask(os, "sr6-other").Charge(1m).ContinueWith(_ => { }, Ct, TaskContinuationOptions.None, TaskScheduler.Default);
        await os.Idle();

        Assert.Equal(0, os.GateRestores);
        Assert.Null(await Sources(os, "sr6").Get(HsSource.Key, Ct));
    }

    [Fact]
    public async Task TryRestoreOfALiveAliasIsTrue()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr7");
        var emits = os.Synapses.Compiles;

        Assert.True(await os.Restorer.TryRestore(Brain("sr7"), "hs.week", Ct));
        Assert.Equal(emits, os.Synapses.Compiles);
    }

    [Fact]
    public async Task AFailedActivateCanBeRetried()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr8");
        await os.Restart();
        var restorer = new DigitalBrain.Runtime.Synapses.SynapseRestorer(new DigitalBrain.Runtime.Synapses.SynapseLoader(), new FailOnce(os.Registrar), os.Store, os.Source);

        await Assert.ThrowsAsync<InvalidOperationException>(() => restorer.TryRestore(Brain("sr8"), "hs.week", Ct));
        Assert.DoesNotContain("hs.week", os.Catalog.States.Keys);

        Assert.True(await restorer.TryRestore(Brain("sr8"), "hs.week", Ct));
        Assert.Contains("hs.week", os.Catalog.States.Keys);
    }

    // Fails the first activation, then passes through.
    private sealed class FailOnce(DigitalBrain.Spi.ISynapseRegistrar inner) : DigitalBrain.Spi.ISynapseRegistrar
    {
        private int calls;

        public Task Activate(BrainId brain, LoadedSource source, CancellationToken ct) =>
            Interlocked.Increment(ref calls) == 1 ? throw new InvalidOperationException("activate failed") : inner.Activate(brain, source, ct);

        public Task Deactivate(BrainId brain, string sourceKey, IReadOnlyList<string> aliases, CancellationToken ct) => inner.Deactivate(brain, sourceKey, aliases, ct);
    }

    [Fact]
    public async Task ARestoreOfAnUnknownAliasIsFalse()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr5");

        Assert.False(await os.Restorer.TryRestore(Brain("sr5"), "hs.nothing", Ct));
    }
}

[Signal("sr.charge-asked")]
public sealed record ChargeAsked(decimal Amount) : Signal;

// As HsPayments, but it first journals that a charge was asked: that publish passes the gate path, which restores the installed
// alias missing after a restart, so the generated Charging type is in the catalog when the charge is made.
[Neuron("sr.payments")]
public sealed class SrPayments : Neuron<HsPayments.Count>, IHsPayments
{
    public async Task Charge(decimal amount)
    {
        await Save(State, new ChargeAsked(amount));
        var type = Context.Services.GetService(typeof(ISynapseCatalogSource)) is ISynapseCatalogSource source
            ? source.Current.For("hs.week").First(d => d.Kind == SynapseKind.Gate).Signal!
            : throw new InvalidOperationException("no catalog");
        await Save(State with { N = State.N + 1 }, (Signal)Activator.CreateInstance(type, amount)!);
    }
}
