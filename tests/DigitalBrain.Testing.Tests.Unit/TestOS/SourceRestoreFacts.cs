using DigitalBrain.Neurons;
using DigitalBrain.Rules;
using DigitalBrain.Kernel;
using DigitalBrain.Units;
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

    private static async Task<decimal> Spent(OS os, string brain)
    {
        var state = await ((IBrainObserver)os).State(NeuronId.Parse($"{brain}/hs.week/default"), Ct);
        return state!.Value.GetProperty("spent").GetDecimal();
    }

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

        var deployment = os.Get<IUnitDeployment>(NeuronId.Parse($"sr1/synapse.deployment/{HsSource.Key}"));
        var record = await deployment.Build(outcome.Hash!, Ct);

        Assert.NotNull(record);
        Assert.Equal(outcome.Hash, record.Ref.Hash);
        Assert.NotEmpty(record.Image.Assembly);
        Assert.Equal(["hs.week"], record.Aliases);
        Assert.Equal(HsSource.Key, await Sources(os, "sr1").OwnerOf("hs.week", Ct));
    }

    [Fact]
    public async Task AfterRestartTheFirstHitRestoresTheSource()
    {
        await using var os = Start();
        var outcome = await DeployApproved(os, "sr2");
        await Ask(os, "sr2").Charge(250m);
        await os.Idle();

        await RestartAndTouch(os, "sr2");
        await Ask(os, "sr2").Charge(100m);
        await os.Idle();

        Assert.Equal(350m, await Spent(os, "sr2"));
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
        var emits = ((DigitalBrain.Runtime.Synapses.UnitDeployer)os.Synapses).Compiles;

        await RestartAndTouch(os, "sr3");

        Assert.Equal(emits, ((DigitalBrain.Runtime.Synapses.UnitDeployer)os.Synapses).Compiles);
        using var handle = await os.Resolver.Resolve(Brain("sr3"), "hs.week", UnitView.Live, Ct);
        Assert.NotNull(handle);
    }

    [Fact]
    public async Task RetireRemovesTheRecord()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr4");

        Assert.Equal(DeployStatus.Retired, (await os.Synapses.Retire(Brain("sr4"), HsSource.Key, Ct)).Status);

        var deployment = os.Get<IUnitDeployment>(NeuronId.Parse($"sr4/synapse.deployment/{HsSource.Key}"));
        Assert.Null(await deployment.Ref(UnitView.Live, Ct));
        Assert.Null(await Sources(os, "sr4").OwnerOf("hs.week", Ct));
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

        Assert.Null(await Sources(os, "sr6").OwnerOf("hs.week", Ct));
    }

    [Fact]
    public async Task TryRestoreOfALiveAliasIsTrue()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr7");
        var emits = ((DigitalBrain.Runtime.Synapses.UnitDeployer)os.Synapses).Compiles;

        using var handle = await os.Resolver.Resolve(Brain("sr7"), "hs.week", UnitView.Live, Ct);
        Assert.NotNull(handle);
        Assert.Equal(emits, ((DigitalBrain.Runtime.Synapses.UnitDeployer)os.Synapses).Compiles);
    }

    [Fact]
    public async Task ResolvingAfterRestartSucceedsWithoutRecompile()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr8");
        await os.Restart();

        using var handle = await os.Resolver.Resolve(Brain("sr8"), "hs.week", UnitView.Live, Ct);
        Assert.NotNull(handle);
        Assert.Contains("hs.week", handle.Catalog.States.Keys);
    }

    [Fact]
    public async Task AStoredBuildOutsideTheBoundaryIsNotRestoredAndIsListedAsRebuildRequired()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr9");
        var deployment = os.Get<IUnitDeployment>(NeuronId.Parse($"sr9/synapse.deployment/{HsSource.Key}"));
        var liveRef = (await deployment.Ref(UnitView.Live, Ct))!;

        await deployment.Refuse(liveRef.Value.Hash, "System.Runtime.CompilerServices.RuntimeHelpers.Box", Ct);
        await os.Idle();

        Assert.Null(await os.Resolver.Resolve(Brain("sr9"), "hs.week", UnitView.Live, Ct));

        var listed = Assert.Single(await os.Synapses.List(Brain("sr9"), Ct));
        Assert.Equal(DeployStatus.RebuildRequired, listed.Status);
        Assert.Contains("System.Runtime.CompilerServices.RuntimeHelpers.Box", listed.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecordWithoutSlotsStillResolves()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr11");
        await os.Restart();

        using var handle = await os.Resolver.Resolve(Brain("sr11"), "hs.week", UnitView.Live, Ct);
        Assert.NotNull(handle);
    }

    [Fact]
    public async Task ApprovingAnAskRaisedBeforeARestartRestoresTheAliasAndInstallsIt()
    {
        await using var os = Start();
        var outcome = await os.Synapses.Deploy(Brain("sr12"), HsSource.Of(HsSource.Budget), None, Ct);
        Assert.Equal(InstallStatus.Pending, outcome.Installs["hs.week"].Status);
        await os.Idle();

        await os.Restart();
        await os.Confirmations.Approve(outcome.Installs["hs.week"].Reason!);
        await os.Idle();

        var journal = os.Journal(Brain("sr12")).Signals(NeuronId.Parse("sr12/synapse.installs/default"));
        Assert.DoesNotContain(journal, s => s.Signal is InstallDenied);
        Assert.Contains(journal, s => s.Signal is Installed { Alias: "hs.week" });
        using var handle = await os.Resolver.Resolve(Brain("sr12"), "hs.week", UnitView.Live, Ct);
        Assert.NotNull(handle);
    }

    [Fact]
    public async Task AnInstallPlanCarriesItsKindAndAlias()
    {
        await using var os = Start();
        var outcome = await os.Synapses.Deploy(Brain("sr13"), HsSource.Of(HsSource.Budget), None, Ct);
        await os.Idle();

        var asked = Assert.Single(os.Confirmations.Waiting, r => r.PlanId == outcome.Installs["hs.week"].Reason);
        Assert.Equal(DigitalBrain.Confirmation.ConfirmationPlan.SynapseInstallKind, asked.Plan.Kind);
        Assert.Equal("synapse.install", asked.Plan.Kind);
        Assert.Equal("hs.week", asked.Plan.Subject);
        Assert.StartsWith("install:hs.week:", asked.PlanId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARestoreOfAnUnknownAliasIsFalse()
    {
        await using var os = Start();
        _ = await DeployApproved(os, "sr5");

        Assert.Null(await os.Resolver.Resolve(Brain("sr5"), "hs.nothing", UnitView.Live, Ct));
    }
}

[Signal("sr.charge-asked")]
public sealed record ChargeAsked(decimal Amount) : Signal;

// As HsPayments, but it first journals that a charge was asked: that publish passes the gate path, which restores the installed
// alias missing after a restart, so the generated Charging type is in the catalog when the charge is made.
[Neuron("sr.payments")]
public sealed class SrPayments(ISynapseCatalogSource? catalogSource = null, UnitResolver? resolver = null) : Neuron<HsPayments.Count>, IHsPayments
{
    public async Task Charge(decimal amount)
    {
        await Save(State, new ChargeAsked(amount));
        Type? type = null;
        if (resolver is not null)
        {
            using var handle = await resolver.Resolve(Id.Brain, "hs.week", UnitView.Live, CancellationToken.None).ConfigureAwait(true);
            type = handle?.Catalog.For("hs.week").FirstOrDefault(d => d.Kind == SynapseKind.Reaction)?.Signal;
        }
        type ??= (catalogSource ?? throw new InvalidOperationException("no catalog"))
            .Static.For("hs.week").First(d => d.Kind == SynapseKind.Reaction).Signal!;
        await Save(State with { N = State.N + 1 }, (Signal)Activator.CreateInstance(type, amount)!);
    }
}
