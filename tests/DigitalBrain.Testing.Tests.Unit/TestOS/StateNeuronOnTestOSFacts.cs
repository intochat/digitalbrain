using System.Collections.Concurrent;
using DigitalBrain.Spi;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Synapses;

[Signal("test.synapse.charging")]
public sealed record Charging(decimal Amount) : Signal;

[State("week")]
public sealed record WeekSpend(decimal Spent)
{
    public WeekSpend() : this(0m) { }
}

[State("ledger")]
public sealed record Ledger(decimal Spent)
{
    public Ledger() : this(0m) { }
}

public sealed record PayState
{
    public int Count { get; init; }
}

public static class Budget
{
    [Synapse(typeof(Charging), "week")]
    public static WeekSpend WeeklyCap(Charging s, WeekSpend state) =>
        state with { Spent = state.Spent + s.Amount };

    [Synapse("week")]
    public static DigitalBrain.UI.View Show(WeekSpend w) => new("week", new DigitalBrain.UI.Text($"{w.Spent} of 300"));
}

public static class Books
{
    [Synapse(typeof(Charging), "ledger")]
    public static Ledger Add(Charging s, Ledger w) => w with { Spent = w.Spent + s.Amount };
}

public interface IPayments
{
    Task Charge(decimal amount);
}

[Neuron("payments")]
public sealed class Payments : Neuron<PayState>, IPayments
{
    public Task Charge(decimal amount) => Save(State with { Count = State.Count + 1 }, new Charging(amount));
}

public sealed class StateNeuronOnTestOSFacts
{
    private static readonly NeuronId Pay = NeuronId.Parse("b/payments/p");
    private static readonly NeuronId Week = NeuronId.Parse("b/week/default");
    private static readonly NeuronId Books = NeuronId.Parse("b/ledger/default");

    private static OS Start(Action<TestOSOptions>? configure = null)
    {
        var os = OS.Local(configure).Register(typeof(StateNeuronOnTestOSFacts).Assembly);
        os.Confirmations.ApproveAll();
        return os;
    }

    // Reaction edges come from the install: each fact installs the aliases it charges.
    internal static async Task Install(OS os, string alias)
    {
        await os.Get<IInstalls>(NeuronId.Parse("b/synapse.installs/default")).Install(new(alias, new Dictionary<string, string>()), TestContext.Current.CancellationToken);
        await os.Idle();
    }

    [Fact]
    public async Task AReactionSynapseIsEnsuredAndDeliversOnce()
    {
        await using var os = Start();
        await Install(os, "ledger");
        var pay = os.Get<IPayments>(Pay);
        await os.Activate(Books);
        await os.Idle();

        await pay.Charge(5m);
        await os.Idle();

        Assert.Equal(5m, await Spent(os));
        Assert.Contains(os.SynapsesOf(Books), s => s.From == new NeuronId(Pay.Brain, "*", "*") && s.Signal == typeof(Charging));

        await os.Restart();
        await os.Activate(Books);
        await os.Idle();

        Assert.Equal(5m, await Spent(os));
        Assert.Single(os.Journal(Books.Brain).Signals(Books), s => s.Signal is StateChanged);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task ViewChangedIsOfferedAfterAnAllowedCharge()
    {
        await using var os = Start();
        await Install(os, "week");
        var heard = new ConcurrentQueue<string>();
        await using var listener = await os.ClusterListeners.Listen<ViewChanged>("views", (_, changed, _) =>
        {
            heard.Enqueue(changed.Alias);
            return Task.CompletedTask;
        });

        await os.Get<IPayments>(Pay).Charge(10m);
        await os.Idle();

        Assert.Contains("week", heard);
    }

    private static async Task<decimal> Spent(OS os)
    {
        var state = await ((IBrainObserver)os).State(Books, TestContext.Current.CancellationToken);
        return state!.Value.GetProperty("spent").GetDecimal();
    }
}
