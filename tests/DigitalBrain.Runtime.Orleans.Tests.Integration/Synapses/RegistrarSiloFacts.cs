using System.Reflection;
using DigitalBrain.Invocation;
using DigitalBrain.Rules;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

// Plan 1's weekly-cap file with its own aliases and a reaction that publishes a generated signal.
internal static class RsSource
{
    internal const string Budget = """
        namespace Me.Budget;

        [Signal("rs.charging")]
        public sealed record Charging(decimal Amount) : Signal;

        [Signal("rs.week-rolled")]
        public sealed record WeekRolled(DateOnly Week) : Signal;

        [Signal("rs.report-ready")]
        public sealed record ReportReady(DateOnly Week, decimal Spent) : Signal;

        [State("rs.week")]
        public sealed record WeekSpend(DateOnly Week = default, decimal Spent = 0)
        {
            public WeekSpend() : this(default, 0) { }
        }

        public static class Budget
        {
            [Synapse(typeof(Charging), "rs.week")]
            public static WeekSpend WeeklyCap(Charging s, WeekSpend t) =>
                t with { Spent = t.Spent + s.Amount };

            [Synapse(typeof(WeekRolled), "rs.week")]
            public static Reaction<WeekSpend> NewWeek(WeekRolled s, WeekSpend t) =>
                Reaction<WeekSpend>.Of(new WeekSpend(s.Week, 0), new ReportReady(t.Week, t.Spent));

            [Synapse("rs.week")]
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

public interface IRsPayments
{
    Task Charge(decimal amount);

    Task Roll(DateOnly week);
}

// Publishes the generated signals of RsPayments.Source, built by reflection: the test assembly cannot name them.
[Neuron("rs.payments")]
public sealed class RsPayments : Neuron<RsPayments.Count>, IRsPayments
{
    internal static LoadedSource? Source { get; set; }

    public Task Charge(decimal amount) => Save(State with { N = State.N + 1 }, RsSource.Make(Source!, "Charging", amount));

    public Task Roll(DateOnly week) => Save(State with { N = State.N + 1 }, RsSource.Make(Source!, "WeekRolled", week));

    public sealed record Count
    {
        public int N { get; init; }
    }
}

public sealed class RegistrarSiloFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ISynapseRegistrar Registrar => silo.Services.GetRequiredService<ISynapseRegistrar>();

    private async Task<LoadedSource> Start(string brain, string text = RsSource.Budget)
    {
        var source = RsSource.Load(text);
        RsPayments.Source = source;
        await Registrar.Activate(Brain(brain), source, Ct);
        await StateNeuronSiloFacts.Install(silo.Services, brain, "rs.week");
        return source;
    }

    private static BrainId Brain(string brain) => NeuronId.Parse($"{brain}/x/y").Brain;

    private async Task<Signal?> Report(string brain)
    {
        var week = NeuronId.Parse($"{brain}/rs.week/default");
        Signal? found = null;
        await StateNeuronSiloFacts.Until(async () =>
        {
            found = (await silo.History(brain).Read(week, 0, 50, Ct)).Signals.Select(s => s.Signal).FirstOrDefault(s => s.GetType().Name == "ReportReady");
            return found is not null;
        }, Ct);
        return found;
    }

    [Fact]
    public async Task ActivateMakesTheAliasHostableAndItsSignalsJournalable()
    {
        var source = await Start("rsa");
        var pay = silo.Get<IRsPayments>("rsa/rs.payments/p");

        await pay.Charge(250m);
        await pay.Roll(new DateOnly(2026, 1, 5));
        var report = await Report("rsa");

        Assert.NotNull(report);
        Assert.Same(source.Assembly, report.GetType().Assembly);
        Assert.Equal("rs.report-ready", report.GetType().GetCustomAttribute<SignalAttribute>()?.Alias);
        Assert.Equal(250m, report.GetType().GetProperty("Spent")!.GetValue(report));
    }

    [Fact]
    public async Task DeactivateRemovesTheAliasAndTheSignal()
    {
        var source = await Start("rsd");
        var week = NeuronId.Parse("rsd/rs.week/default");
        var host = silo.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, week.ToString()));
        _ = await host.InspectState(Ct);

        await Registrar.Deactivate(week.Brain, source.SourceKey, ["rs.week"], Ct);

        Assert.False(silo.Services.GetRequiredService<NeuronTypeRegistry>().TryGetRegistration("rs.week", out _));
        Assert.False(silo.Services.GetRequiredService<ISynapseCatalogSource>().Current.States.ContainsKey("rs.week"));
        Assert.False(silo.Services.GetRequiredService<JournalPayloads>().Types.TryResolve("rs.report-ready", out _));
        await StateNeuronSiloFacts.Until(async () =>
        {
            try { _ = await host.InspectState(Ct); return false; }
            catch (BindingException) { return true; }
        }, Ct);
        await Assert.ThrowsAsync<BindingException>(() => host.InspectState(Ct));
    }

    [Fact]
    public async Task AReplacedSignalTypeDecodesOldEntries()
    {
        var a = await Start("rsr");
        await silo.Get<IRsPayments>("rsr/rs.payments/p").Charge(40m);
        await silo.Get<IRsPayments>("rsr/rs.payments/p").Roll(new DateOnly(2026, 1, 5));
        var old = await Report("rsr");
        Assert.Same(a.Assembly, old?.GetType().Assembly);

        var b = RsSource.Load(RsSource.Budget.Replace("This week", "Current week", StringComparison.Ordinal));
        Assert.NotEqual(a.Hash, b.Hash);
        await Registrar.Activate(Brain("rsr"), b, Ct);
        var decoded = await Report("rsr");

        Assert.NotNull(decoded);
        Assert.Same(b.Assembly, decoded.GetType().Assembly);
        Assert.Equal(old!.GetType().GetProperty("Week")!.GetValue(old), decoded.GetType().GetProperty("Week")!.GetValue(decoded));
        Assert.Equal(40m, decoded.GetType().GetProperty("Spent")!.GetValue(decoded));
    }
}
