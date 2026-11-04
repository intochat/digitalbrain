using DigitalBrain.Rules;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

// The weekly-cap file of SynapseCompilerFacts (Runtime.Synapses unit tests), copied with a reaction that publishes a generated
// signal, deployed through the silo's synapse host. The silo's catalog and loader are shared by its brains, so each fact's brain
// deploys its own copy: the brain name prefixes the aliases ("sh1.week") and suffixes the source key.
internal static class ShSource
{

    internal const string Budget = """
        namespace Me.SiloBudget;

        [Signal("sh.charging")]
        public sealed record Charging(decimal Amount) : Signal;

        [Signal("sh.week-rolled")]
        public sealed record WeekRolled(DateOnly Week) : Signal;

        [Signal("sh.report-ready")]
        public sealed record ReportReady(DateOnly Week, decimal Spent) : Signal;

        [State("sh.week")]
        public sealed record WeekSpend(DateOnly Week = default, decimal Spent = 0)
        {
            public WeekSpend() : this(default, 0) { }
        }

        public static class Budget
        {
            [Synapse(typeof(Charging), "sh.week")]
            public static Check WeeklyCap(Charging s, WeekSpend t) =>
                t.Spent + s.Amount > 300m
                    ? Check.Reject("weekly cap of $300")
                    : Check.Allow(t with { Spent = t.Spent + s.Amount });

            [Synapse(typeof(WeekRolled), "sh.week")]
            public static Reaction<WeekSpend> NewWeek(WeekRolled s, WeekSpend t) =>
                Reaction<WeekSpend>.Of(new WeekSpend(s.Week, 0), new ReportReady(t.Week, t.Spent));

            [Synapse("sh.week")]
            public static View Show(WeekSpend t) =>
                new("This week", new Column([new StatCardNode("spent", "Spent", $"{t.Spent} of 300")]));
        }
        """;

    // Need-neutral: only the cap changes.
    internal static readonly string Cap200 = Budget.Replace("> 300m", "> 200m", StringComparison.Ordinal);

    // A new need: the gate takes a sql-read slot.
    internal static readonly string WithDb = Budget
        .Replace("namespace Me.SiloBudget;", "using DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;\n\nnamespace Me.SiloBudget;", StringComparison.Ordinal)
        .Replace("WeeklyCap(Charging s, WeekSpend t)", "WeeklyCap(Charging s, WeekSpend t, [Slot] ISqlRead Db)", StringComparison.Ordinal);

    internal static string Key(string brain) => "Me.SiloBudget" + brain;

    internal static string Week(string brain) => brain + ".week";

    internal static SynapseSource Of(string brain, string text) =>
        new(Key(brain), new Dictionary<string, string> { ["Budget.cs"] = text.Replace("\"sh.", $"\"{brain}.", StringComparison.Ordinal) }, null);

    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> None => new Dictionary<string, IReadOnlyDictionary<string, string>>();

    internal static BrainId Brain(string name) => NeuronId.Parse($"{name}/x/y").Brain;
}

public interface IShPayments
{
    Task Charge(decimal amount);

    Task Roll(DateOnly week);
}

[Signal("test.sh.charge-asked")]
public sealed record ShChargeAsked(decimal Amount) : Signal;

// Publishes the live generated signals: their types are read from the catalog on each call, so no fact pins an old context. Each
// call first journals a plain signal; that publish passes the gate path, which restores an installed alias missing after a restart.
[Neuron("sh.payments")]
public sealed class ShPayments : Neuron<SbPayState>, IShPayments
{
    public Task Charge(decimal amount) => Publish(SynapseKind.Gate, amount);

    public Task Roll(DateOnly week) => Publish(SynapseKind.Reaction, week);

    private async Task Publish(SynapseKind kind, object value)
    {
        await Save(State, new ShChargeAsked(0m));
        var source = (ISynapseCatalogSource)(Context.Services.GetService(typeof(ISynapseCatalogSource)) ?? throw new InvalidOperationException("no catalog"));
        var type = source.Current.For(Id.Brain + ".week").First(d => d.Kind == kind && d.Signal is not null).Signal!;
        await Save(State with { Count = State.Count + 1 }, (Signal)Activator.CreateInstance(type, value)!);
    }
}

public sealed class SynapseHostSiloFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SynapseHost Host => (SynapseHost)silo.Services.GetRequiredService<ISynapseHost>();

    private IShPayments Pay(string brain) => silo.Get<IShPayments>($"{brain}/sh.payments/p");

    private IInstalls Installs(string brain) => silo.Get<IInstalls>($"{brain}/synapse.installs/default");

    // Deploys the weekly cap; the fixture approves its install at once.
    private async Task<DeployOutcome> Deploy(string brain)
    {
        var outcome = await Host.Deploy(ShSource.Brain(brain), ShSource.Of(brain, ShSource.Budget), ShSource.None, Ct);
        Assert.True(outcome.Status == DeployStatus.Deployed, $"{outcome.Status}: {outcome.Reason} {string.Join("; ", outcome.Diagnostics)}");
        await StateNeuronSiloFacts.Until(async () => await Installs(brain).Describe(ShSource.Week(brain), Ct) is not null, Ct);
        Assert.NotNull(await Installs(brain).Describe(ShSource.Week(brain), Ct));
        return outcome;
    }

    private async Task<Signal?> Report(string brain)
    {
        var week = NeuronId.Parse($"{brain}/{ShSource.Week(brain)}/default");
        Signal? found = null;
        await StateNeuronSiloFacts.Until(async () =>
        {
            found = (await silo.History(brain).Read(week, 0, 50, Ct)).Signals.Select(s => s.Signal).FirstOrDefault(s => s.GetType().Name == "ReportReady");
            return found is not null;
        }, Ct);
        return found;
    }

    [Fact]
    public async Task DeployAndChargeOnTheSilo()
    {
        var outcome = await Deploy("sh1");

        await Pay("sh1").Charge(250m);
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => Pay("sh1").Charge(100m));

        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
        var listed = Assert.Single(await Host.List(ShSource.Brain("sh1"), Ct));
        Assert.Equal((DeployStatus.Deployed, outcome.Hash), (listed.Status, listed.Hash));
        Assert.Equal(DeployStatus.Unchanged, (await Host.Deploy(ShSource.Brain("sh1"), ShSource.Of("sh1", ShSource.Budget), ShSource.None, Ct)).Status);
    }

    [Fact]
    public async Task ANeedNeutralUpdateSwapsAndUnloads()
    {
        await Deploy("sh2");
        Assert.Equal("weekly cap of $300", (await Assert.ThrowsAsync<RuleViolationException>(() => Pay("sh2").Charge(350m))).Rejected.Messages[0]);

        var update = await Host.Deploy(ShSource.Brain("sh2"), ShSource.Of("sh2", ShSource.Cap200), ShSource.None, Ct);

        Assert.Equal(DeployStatus.Deployed, update.Status);
        Assert.Equal(InstallStatus.Unchanged, update.Installs[ShSource.Week("sh2")].Status);
        await Assert.ThrowsAsync<RuleViolationException>(() => Pay("sh2").Charge(250m));
        Assert.Equal(update.Hash, Assert.Single(await Host.List(ShSource.Brain("sh2"), Ct)).Hash);
        // The retired version's signal types left the silo's signal map and its neuron class was not cached, so its context collects.
        Assert.Empty(Host.Leaked);
    }

    [Fact]
    public async Task RestartRestoresTheGateBeforeTheFirstPublish()
    {
        var outcome = await Deploy("sh3");
        await Pay("sh3").Charge(250m);

        await silo.Restart(Ct);
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => Pay("sh3").Charge(100m));

        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
        var listed = Assert.Single(await Host.List(ShSource.Brain("sh3"), Ct));
        Assert.Equal((DeployStatus.Deployed, outcome.Hash), (listed.Status, listed.Hash));
    }

    [Fact]
    public async Task AReplacedSignalDecodesOldEntries()
    {
        await Deploy("sh4");
        await Pay("sh4").Charge(40m);
        await Pay("sh4").Roll(new DateOnly(2026, 1, 5));
        var old = await Report("sh4");
        Assert.NotNull(old);
        var oldWeek = old.GetType().GetProperty("Week")!.GetValue(old);
        var oldAssembly = old.GetType().Assembly.FullName;
        old = null;

        var update = await Host.Deploy(ShSource.Brain("sh4"), ShSource.Of("sh4", ShSource.Cap200), ShSource.None, Ct);
        Assert.Equal(DeployStatus.Deployed, update.Status);
        var decoded = await Report("sh4");

        Assert.NotNull(decoded);
        Assert.NotEqual(oldAssembly, decoded.GetType().Assembly.FullName);
        Assert.Equal(oldWeek, decoded.GetType().GetProperty("Week")!.GetValue(decoded));
        Assert.Equal(40m, decoded.GetType().GetProperty("Spent")!.GetValue(decoded));
    }
}

// On SlotSilo: the staged version's gate takes a sql-read slot bound to a connection the brain lists; the fixture approves at once.
public sealed class SynapseHostStagingSiloFacts(SlotSilo silo) : IClassFixture<SlotSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SynapseHost Host => (SynapseHost)silo.Services.GetRequiredService<ISynapseHost>();

    [Fact]
    public async Task AStagedUpdateSwapsOnApproval()
    {
        var brain = ShSource.Brain("sh5");
        var installs = silo.Get<IInstalls>("sh5/synapse.installs/default");
        var first = await Host.Deploy(brain, ShSource.Of("sh5", ShSource.Budget), ShSource.None, Ct);
        Assert.Equal(DeployStatus.Deployed, first.Status);
        await StateNeuronSiloFacts.Until(async () => await installs.Describe(ShSource.Week("sh5"), Ct) is not null, Ct);
        var bindings = new Dictionary<string, IReadOnlyDictionary<string, string>> { [ShSource.Week("sh5")] = new Dictionary<string, string> { ["Db"] = "analytics" } };

        var staged = await Host.Deploy(brain, ShSource.Of("sh5", ShSource.WithDb), bindings, Ct);
        Assert.Equal(DeployStatus.Staged, staged.Status);
        await StateNeuronSiloFacts.Until(async () => Assert.Single(await Host.List(brain, Ct)).Status == DeployStatus.Deployed, Ct);

        var listed = Assert.Single(await Host.List(brain, Ct));
        Assert.Equal((DeployStatus.Deployed, staged.Hash, (string?)null), (listed.Status, listed.Hash, listed.StagedHash));
        var catalog = silo.Services.GetRequiredService<ISynapseCatalogSource>().Current;
        Assert.NotEmpty(catalog.NeedsOf(ShSource.Week("sh5")).Slots);
        Assert.Equal(catalog.NeedsOf(ShSource.Week("sh5")).Hash(), (await installs.Describe(ShSource.Week("sh5"), Ct))?.NeedsHash);
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => silo.Get<IShPayments>("sh5/sh.payments/p").Charge(350m));
        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
    }
}
