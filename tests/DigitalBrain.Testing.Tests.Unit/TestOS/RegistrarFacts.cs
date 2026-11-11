using DigitalBrain.Neurons;
using DigitalBrain.Units;
using System.Reflection;
using DigitalBrain.Rules;
using DigitalBrain.Runtime.Synapses;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Registrar;

// Plan 1's weekly-cap file with its own aliases and a reaction that publishes a generated signal.
internal static class RgSource
{
    internal const string Budget = """
        namespace Me.Budget;

        [Signal("rg.charging")]
        public sealed record Charging(decimal Amount) : Signal;

        [Signal("rg.week-rolled")]
        public sealed record WeekRolled(DateOnly Week) : Signal;

        [Signal("rg.report-ready")]
        public sealed record ReportReady(DateOnly Week, decimal Spent) : Signal;

        [State("rg.week")]
        public sealed record WeekSpend(DateOnly Week = default, decimal Spent = 0)
        {
            public WeekSpend() : this(default, 0) { }
        }

        public static class Budget
        {
            [Synapse(typeof(Charging), "rg.week")]
            public static WeekSpend WeeklyCap(Charging s, WeekSpend t) =>
                t with { Spent = t.Spent + s.Amount };

            [Synapse(typeof(WeekRolled), "rg.week")]
            public static Reaction<WeekSpend> NewWeek(WeekRolled s, WeekSpend t) =>
                Reaction<WeekSpend>.Of(new WeekSpend(s.Week, 0), new ReportReady(t.Week, t.Spent));

            [Synapse("rg.week")]
            public static View Show(WeekSpend t) =>
                new("This week", new Column([new StatCardNode("spent", "Spent", $"{t.Spent} of 300")]));
        }
        """;

    internal static LoadedSource Load(string text = Budget)
    {
        var build = new SynapseCompiler(ReferenceSet.FromDefaultContext()).Compile(
            new SynapseSource("Me.Budget", new Dictionary<string, string> { ["Budget.cs"] = text }, null));
        Assert.True(build.Success, string.Join("\n", build.Diagnostics));
        return new SynapseLoader().Load(build);
    }

    internal static Signal Make(LoadedSource source, string name, params object[] args)
        => (Signal)Activator.CreateInstance(source.Signals.Single(t => t.Name == name), args)!;
}

public interface IRgPayments
{
    Task Charge(decimal amount);

    Task Roll(DateOnly week);
}

// Publishes the generated signals of RgPayments.Source, built by reflection: the test assembly cannot name them.
[Neuron("rg.payments")]
public sealed class RgPayments : Neuron<RgPayments.Count>, IRgPayments
{
    internal static LoadedSource? Source { get; set; }

    public Task Charge(decimal amount) => Save(State with { N = State.N + 1 }, RgSource.Make(Source!, "Charging", amount));

    public Task Roll(DateOnly week) => Save(State with { N = State.N + 1 }, RgSource.Make(Source!, "WeekRolled", week));

    public sealed record Count
    {
        public int N { get; init; }
    }
}

public sealed class RegistrarFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<OS> Start(string brain, LoadedSource source)
    {
        var os = OS.Local().Register<RgPayments>();
        os.Confirmations.ApproveAll();
        RgPayments.Source = source;
        await os.Registrar.Activate(NeuronId.Parse($"{brain}/x/y").Brain, source, Ct);
        await os.Get<IInstalls>(NeuronId.Parse($"{brain}/synapse.installs/default")).Install(new InstallRequest("rg.week", new Dictionary<string, string>()), Ct);
        await os.Idle();
        return os;
    }

    [Fact]
    public async Task ActivateMakesTheAliasHostableAndItsSignalsJournalable()
    {
        var source = RgSource.Load();
        await using var os = await Start("rga", source);
        var pay = os.Get<IRgPayments>(NeuronId.Parse("rga/rg.payments/p"));

        await pay.Charge(250m);
        await pay.Roll(new DateOnly(2026, 1, 5));
        await os.Idle();

        var week = NeuronId.Parse("rga/rg.week/default");
        var report = Assert.Single(os.Journal(week.Brain).Signals(week), s => s.Signal.GetType().Name == "ReportReady").Signal;
        Assert.Same(source.Assembly, report.GetType().Assembly);
        Assert.Equal("rg.report-ready", report.GetType().GetCustomAttribute<SignalAttribute>()?.Alias);
        Assert.Equal(250m, report.GetType().GetProperty("Spent")!.GetValue(report));
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task DeactivateRemovesTheAliasAndTheSignal()
    {
        var source = RgSource.Load();
        await using var os = await Start("rgd", source);
        var week = NeuronId.Parse("rgd/rg.week/default");
        await os.Activate(week);

        await os.Registrar.Deactivate(week.Brain, source.SourceKey, ["rg.week"], Ct);

        Assert.False(os.IsActive(week));
        Assert.False(os.Catalog.States.ContainsKey("rg.week"));
        Assert.Throws<InvalidOperationException>(() => os.Get<IInstalls>(week));
        await os.Get<IRgPayments>(NeuronId.Parse("rgd/rg.payments/p")).Charge(400m);
        await os.Idle();
        Assert.Empty(os.Failures);
    }
}
