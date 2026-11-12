using DigitalBrain.Modules;
using DigitalBrain.Neurons;
using DigitalBrain.Kernel;
using DigitalBrain.Units;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Tests.Unit.Slots;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Host;

// Plan 1's weekly-cap file with its own aliases, deployed through the host as generated source.
internal static class HsSource
{
    internal const string Key = "Me.HostBudget";

    internal const string Budget = """
        namespace Me.HostBudget;

        [Signal("hs.charging")]
        public sealed record Charging(decimal Amount) : Signal;

        [Signal("hs.week-rolled")]
        public sealed record WeekRolled(DateOnly Week) : Signal;

        [Signal("hs.report-ready")]
        public sealed record ReportReady(DateOnly Week, decimal Spent) : Signal;

        [State("hs.week")]
        public sealed record WeekSpend(DateOnly Week = default, decimal Spent = 0)
        {
            public WeekSpend() : this(default, 0) { }
        }

        public static class Budget
        {
            [Synapse(typeof(Charging), "hs.week")]
            public static WeekSpend WeeklyCap(Charging s, WeekSpend t) =>
                t with { Spent = t.Spent + s.Amount };

            [Synapse(typeof(WeekRolled), "hs.week")]
            public static Reaction<WeekSpend> NewWeek(WeekRolled s, WeekSpend t) =>
                Reaction<WeekSpend>.Of(new WeekSpend(s.Week, 0), new ReportReady(t.Week, t.Spent));

            [Synapse("hs.week")]
            public static View Show(WeekSpend t) =>
                new("This week", new Column([new StatCardNode("spent", "Spent", $"{t.Spent} of 300")]));
        }
        """;

    // Need-neutral: only the spend accumulation changes.
    internal static readonly string Cap200 = Budget.Replace("Spent = t.Spent + s.Amount", "Spent = t.Spent + s.Amount * 2", StringComparison.Ordinal);

    // A new need: the reaction takes a sql-read slot.
    internal static readonly string WithDb = Budget
        .Replace("namespace Me.HostBudget;", "using DigitalBrain.Testing.Tests.Unit.Slots;\n\nnamespace Me.HostBudget;", StringComparison.Ordinal)
        .Replace("WeeklyCap(Charging s, WeekSpend t)", "WeeklyCap(Charging s, WeekSpend t, [Slot] ISqlRead Db)", StringComparison.Ordinal);

    // A second needs-changing edit on top of WithDb.
    internal static readonly string WithDbCap250 = WithDb.Replace("s.Amount", "s.Amount * 3", StringComparison.Ordinal);

    // A compile error on line 22: the reaction loses its expression.
    internal static readonly string Broken = Budget.Replace("t.Spent + s.Amount", "t.Spent +", StringComparison.Ordinal);

    // Another source key that declares the same state alias.
    internal static readonly string Rival = Budget.Replace("Me.HostBudget", "Me.Rival", StringComparison.Ordinal)
        .Replace("\"hs.charging\"", "\"hs.rival-charging\"", StringComparison.Ordinal)
        .Replace("\"hs.week-rolled\"", "\"hs.rival-rolled\"", StringComparison.Ordinal)
        .Replace("\"hs.report-ready\"", "\"hs.rival-report\"", StringComparison.Ordinal);

    internal static SynapseSource Of(string text, string key = Key) =>
        new(key, new Dictionary<string, string> { ["Budget.cs"] = text }, null);
}

public interface IHsPayments
{
    Task Charge(decimal amount);
}

// Publishes the live generated Charging signal: its type is read from the catalog on each call, so no fact pins an old context.
[Neuron("hs.payments")]
public sealed class HsPayments(ISynapseCatalogSource? catalogSource = null, UnitResolver? resolver = null) : Neuron<HsPayments.Count>, IHsPayments
{
    public async Task Charge(decimal amount)
    {
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

    public sealed record Count
    {
        public int N { get; init; }
    }
}

public sealed class SynapseHostFacts
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DbBindings =
        new Dictionary<string, IReadOnlyDictionary<string, string>> { ["hs.week"] = new Dictionary<string, string> { ["Db"] = "analytics" } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> None => new Dictionary<string, IReadOnlyDictionary<string, string>>();

    private static OS Start() => OS.Local().Register<HsPayments>().With<ISqlRead>("analytics", new FakeSqlRead(1));

    private static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;

    private static IHsPayments Pay(OS os, string brain) => os.Get<IHsPayments>(NeuronId.Parse($"{brain}/hs.payments/p"));

    private static async Task<decimal> Spent(OS os, string brain)
    {
        var state = await ((IBrainObserver)os).State(NeuronId.Parse($"{brain}/hs.week/default"), Ct);
        return state!.Value.GetProperty("spent").GetDecimal();
    }

    private static void Collect()
    {
        for (var i = 0; i < 5; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    // Deploys the weekly cap and approves its first install.
    private static async Task<DeployOutcome> DeployApproved(OS os, string brain)
    {
        var outcome = await os.Synapses.Deploy(Brain(brain), HsSource.Of(HsSource.Budget), None, Ct);
        Assert.Equal(DeployStatus.Deployed, outcome.Status);
        Assert.Equal(InstallStatus.Pending, outcome.Installs["hs.week"].Status);
        await os.Confirmations.Approve(outcome.Installs["hs.week"].Reason!);
        await os.Idle();
        return outcome;
    }

    [Fact]
    public async Task DeployCompilesLoadsInstallsAndRuns()
    {
        await using var os = Start();
        var outcome = await DeployApproved(os, "hs1");
        var pay = Pay(os, "hs1");

        await pay.Charge(250m);
        await pay.Charge(100m);
        await os.Idle();

        Assert.Equal(350m, await Spent(os, "hs1"));
        var listed = Assert.Single(await os.Synapses.List(Brain("hs1"), Ct));
        Assert.Equal("hs.week", Assert.Single(listed.Aliases));
        Assert.Equal(DeployStatus.Deployed, listed.Status);
        Assert.Equal(outcome.Hash, listed.Hash);
        var deployment = os.Get<IUnitDeployment>(NeuronId.Parse($"hs1/synapse.deployment/{HsSource.Key}"));
        Assert.Equal(outcome.Hash, (await deployment.Build(outcome.Hash!, Ct))?.Ref.Hash);
        Assert.Equal(DeployStatus.Unchanged, (await os.Synapses.Deploy(Brain("hs1"), HsSource.Of(HsSource.Budget), None, Ct)).Status);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task ACompileErrorDeploysNothing()
    {
        await using var os = Start();
        var first = await DeployApproved(os, "hs2");

        var broken = await os.Synapses.Deploy(Brain("hs2"), HsSource.Of(HsSource.Broken), None, Ct);

        Assert.Equal(DeployStatus.Failed, broken.Status);
        Assert.Contains(broken.Diagnostics, d => d.File == "Budget.cs" && d.Line == 22);
        Assert.Empty(broken.Installs);
        Collect();
        Assert.Empty(os.Loader.Lingering());
        Assert.Equal(first.Hash, Assert.Single(await os.Synapses.List(Brain("hs2"), Ct)).Hash);
        await Pay(os, "hs2").Charge(50m);
        await os.Idle();
        Assert.Equal(50m, await Spent(os, "hs2"));
    }

    [Fact]
    public async Task ANeedNeutralUpdateSwapsSilently()
    {
        await using var os = Start();
        await DeployApproved(os, "hs3");
        var asked = os.Confirmations.Requested.Count;

        var update = await os.Synapses.Deploy(Brain("hs3"), HsSource.Of(HsSource.Cap200), None, Ct);
        await os.Idle();

        Assert.Equal(DeployStatus.Deployed, update.Status);
        Assert.Equal(InstallStatus.Unchanged, update.Installs["hs.week"].Status);
        Assert.Equal(asked, os.Confirmations.Requested.Count);
        Assert.Equal(update.Hash, Assert.Single(await os.Synapses.List(Brain("hs3"), Ct)).Hash);
        await Pay(os, "hs3").Charge(50m);
        await os.Idle();
        Assert.Equal(100m, await Spent(os, "hs3"));
        Collect();
        Assert.Empty(os.Loader.Lingering());
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task AnUpdateThatAddsASlotIsStagedUntilApproved()
    {
        await using var os = Start();
        var first = await DeployApproved(os, "hs4");

        var update = await os.Synapses.Deploy(Brain("hs4"), HsSource.Of(HsSource.WithDb), DbBindings, Ct);
        await os.Idle();

        Assert.Equal(DeployStatus.Staged, update.Status);
        var install = update.Installs["hs.week"];
        Assert.Equal(InstallStatus.Pending, install.Status);
        var listed = Assert.Single(await os.Synapses.List(Brain("hs4"), Ct));
        Assert.Equal((DeployStatus.Staged, first.Hash, update.Hash), (listed.Status, listed.Hash, listed.StagedHash));
        Assert.Empty(os.Catalog.NeedsOf("hs.week").Slots);
        var asked = Assert.Single(os.Confirmations.Waiting);
        Assert.Equal(install.Reason, asked.PlanId);
        Assert.Contains("through Db", install.Sentence, StringComparison.Ordinal);

        await os.Confirmations.Approve(asked.PlanId);
        await os.Idle();

        listed = Assert.Single(await os.Synapses.List(Brain("hs4"), Ct));
        Assert.Equal((DeployStatus.Deployed, update.Hash, (string?)null), (listed.Status, listed.Hash, listed.StagedHash));
        Assert.NotEmpty(os.Catalog.NeedsOf("hs.week").Slots);
        var deployment4 = os.Get<IUnitDeployment>(NeuronId.Parse($"hs4/synapse.deployment/{HsSource.Key}"));
        Assert.Equal(update.Hash, (await deployment4.Build(update.Hash!, Ct))?.Ref.Hash);
        Collect();
        Assert.Empty(os.Loader.Lingering());
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task ADeniedUpdateKeepsTheOldVersion()
    {
        await using var os = Start();
        var first = await DeployApproved(os, "hs5");
        var update = await os.Synapses.Deploy(Brain("hs5"), HsSource.Of(HsSource.WithDb), DbBindings, Ct);
        Assert.Equal(DeployStatus.Staged, update.Status);

        await os.Confirmations.Deny(update.Installs["hs.week"].Reason!);
        await os.Idle();

        var listed = Assert.Single(await os.Synapses.List(Brain("hs5"), Ct));
        Assert.Equal((DeployStatus.Deployed, first.Hash, (string?)null), (listed.Status, listed.Hash, listed.StagedHash));
        Assert.Empty(os.Catalog.NeedsOf("hs.week").Slots);
        var record = await os.Get<IInstalls>(NeuronId.Parse("hs5/synapse.installs/default")).Describe("hs.week", Ct);
        Assert.Equal(os.Catalog.NeedsOf("hs.week").Hash(), record?.NeedsHash);
        await Pay(os, "hs5").Charge(50m);
        await os.Idle();
        Assert.Equal(50m, await Spent(os, "hs5"));
        Collect();
        Assert.Empty(os.Loader.Lingering());
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task TwoSourcesCannotClaimOneAlias()
    {
        await using var os = Start();
        var first = await DeployApproved(os, "hs6");

        var rival = await os.Synapses.Deploy(Brain("hs6"), HsSource.Of(HsSource.Rival, "Me.Rival"), None, Ct);

        Assert.Equal(DeployStatus.Refused, rival.Status);
        Assert.Contains("'hs.week'", rival.Reason, StringComparison.Ordinal);
        Assert.Contains(HsSource.Key, rival.Reason, StringComparison.Ordinal);
        Assert.Empty(rival.Installs);
        Assert.Equal(first.Hash, Assert.Single(await os.Synapses.List(Brain("hs6"), Ct)).Hash);
        var rivalDeployment = os.Get<IUnitDeployment>(NeuronId.Parse("hs6/synapse.deployment/Me.Rival"));
        Assert.Null(await rivalDeployment.Ref(UnitView.Live, Ct));
        Collect();
        Assert.Empty(os.Loader.Lingering());
        await Pay(os, "hs6").Charge(50m);
        await os.Idle();
        Assert.Equal(50m, await Spent(os, "hs6"));
    }

    [Fact]
    public async Task RetireFreesTheContext()
    {
        await using var os = Start();
        var first = await DeployApproved(os, "hs7");

        var retired = await os.Synapses.Retire(Brain("hs7"), HsSource.Key, Ct);
        await os.Idle();

        Assert.Equal((DeployStatus.Retired, first.Hash), (retired.Status, retired.Hash));
        Assert.False(os.Catalog.States.ContainsKey("hs.week"));
        Assert.Null(await os.Get<IInstalls>(NeuronId.Parse("hs7/synapse.installs/default")).Describe("hs.week", Ct));
        Assert.Empty(await os.Synapses.List(Brain("hs7"), Ct));
        var deployment7 = os.Get<IUnitDeployment>(NeuronId.Parse($"hs7/synapse.deployment/{HsSource.Key}"));
        Assert.Null(await deployment7.Ref(UnitView.Live, Ct));
        Collect();
        Assert.Empty(os.Loader.Lingering());
        Assert.Equal(DeployStatus.Refused, (await os.Synapses.Retire(Brain("hs7"), HsSource.Key, Ct)).Status);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task ADeployOverAStageCancelsItsAsk()
    {
        await using var os = Start();
        await DeployApproved(os, "hs8");
        var installs = NeuronId.Parse("hs8/synapse.installs/default");
        var first = await os.Synapses.Deploy(Brain("hs8"), HsSource.Of(HsSource.WithDb), DbBindings, Ct);
        Assert.Equal(DeployStatus.Staged, first.Status);
        var firstPlan = first.Installs["hs.week"].Reason!;

        var second = await os.Synapses.Deploy(Brain("hs8"), HsSource.Of(HsSource.WithDbCap250), DbBindings, Ct);
        await os.Idle();

        Assert.Equal(DeployStatus.Staged, second.Status);
        var pending = await os.Get<IInstalls>(installs).Pending(Ct);
        Assert.DoesNotContain(pending, p => p.PlanId == firstPlan);
        Assert.Contains(pending, p => p.PlanId == second.Installs["hs.week"].Reason);
        Assert.Contains(os.Journal(installs.Brain).Signals(installs), s => s.Signal is InstallDenied { Reason: "superseded" } d && d.PlanId == firstPlan);
        Assert.Equal(second.Hash, Assert.Single(await os.Synapses.List(Brain("hs8"), Ct)).StagedHash);
        Collect();
        Assert.Empty(os.Loader.Lingering());
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task AnAutoApprovedInstallCompletesWithoutDeadlock()
    {
        await using var os = Start();
        os.Confirmations.ApproveAll();
        var installs = os.Get<IInstalls>(NeuronId.Parse("hs9/synapse.installs/default"));

        var deployed = await os.Synapses.Deploy(Brain("hs9"), HsSource.Of(HsSource.Budget), None, Ct);
        await os.Idle();
        var staged = await os.Synapses.Deploy(Brain("hs9"), HsSource.Of(HsSource.WithDb), DbBindings, Ct);
        await os.Idle();

        Assert.Equal(DeployStatus.Deployed, deployed.Status);
        Assert.Equal(DeployStatus.Staged, staged.Status);
        Assert.Equal(os.Catalog.NeedsOf("hs.week").Hash(), (await installs.Describe("hs.week", Ct))?.NeedsHash);
        var listed = Assert.Single(await os.Synapses.List(Brain("hs9"), Ct));
        Assert.Equal((DeployStatus.Deployed, staged.Hash), (listed.Status, listed.Hash));
        Collect();
        Assert.Empty(os.Loader.Lingering());
        Assert.Empty(os.Failures);
    }
}
