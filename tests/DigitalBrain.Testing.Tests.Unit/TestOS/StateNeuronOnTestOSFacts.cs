using System.Collections.Concurrent;
using DigitalBrain.Rules;
using DigitalBrain.Spi;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Synapses;

[Signal("test.synapse.charging")]
public sealed record Charging(decimal Amount) : Signal;

[Signal("test.synapse.stalling")]
public sealed record Stalling : Signal;

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

[State("hold")]
public sealed record Hold(int Seen)
{
    public Hold() : this(0) { }
}

public sealed record PayState
{
    public int Count { get; init; }
}

public static class Budget
{
    [Synapse(typeof(Charging), "week")]
    public static Check WeeklyCap(Charging s, WeekSpend state) =>
        state.Spent + s.Amount > 300m
            ? Check.Reject("weekly cap of $300")
            : Check.Allow(state with { Spent = state.Spent + s.Amount });

    [Synapse("week")]
    public static string Show(WeekSpend w) => $"{w.Spent} of 300";
}

public static class Books
{
    [Synapse(typeof(Charging), "ledger")]
    public static Ledger Add(Charging s, Ledger w) => w with { Spent = w.Spent + s.Amount };
}

// A gate that never answers on its own: it waits for a completion nobody sets, until its call is cancelled.
public static class Stuck
{
    private static TaskCompletionSource _never = New();
    private static TaskCompletionSource _waiting = New();

    public static Task Waiting => _waiting.Task;

    // Each fact that stalls starts from fresh completions.
    public static void Reset()
    {
        _never = New();
        _waiting = New();
    }

    [Synapse(typeof(Stalling), "hold")]
    public static async Task<Check> Wait(Stalling s, Hold state, CancellationToken ct)
    {
        _waiting.TrySetResult();
        await _never.Task.WaitAsync(ct);
        return Check.Allow(state with { Seen = state.Seen + 1 });
    }

    private static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

[Signal("test.synapse.fragile")]
public sealed record Fragile : Signal;

[State("brittle")]
public sealed record Brittle(int Seen)
{
    public Brittle() : this(0) { }
}

// Brittle allows Fragile; its Committed save then runs Brittle's view, which throws, so Committed fails after the publisher's append.
// (Gates never run on StateChanged, so a gate on it can no longer be the failure.)
public static class Fragility
{
    [Synapse(typeof(Fragile), "brittle")]
    public static Check Allow(Fragile s, Brittle state) => Check.Allow(state with { Seen = state.Seen + 1 });

    [Synapse("brittle")]
    public static string Break(Brittle state) => state.Seen > 0 ? throw new InvalidOperationException("breaker") : "untouched";
}

public interface IPayments
{
    Task Charge(decimal amount);

    Task Stall();

    Task Break();
}

[Neuron("payments")]
public sealed class Payments : Neuron<PayState>, IPayments
{
    public Task Charge(decimal amount) => Save(State with { Count = State.Count + 1 }, new Charging(amount));

    public Task Stall() => Save(State with { Count = State.Count + 1 }, new Stalling());

    public Task Break() => Save(State with { Count = State.Count + 1 }, new Fragile());
}

public sealed class StateNeuronOnTestOSFacts
{
    private static readonly NeuronId Pay = NeuronId.Parse("b/payments/p");
    private static readonly NeuronId Week = NeuronId.Parse("b/week/default");
    private static readonly NeuronId Books = NeuronId.Parse("b/ledger/default");

    private static OS Start(Action<TestOSOptions>? configure = null) => OS.Local(configure).Register(typeof(StateNeuronOnTestOSFacts).Assembly);

    [Fact]
    public async Task ASecondChargeOverTheCapIsRejectedBeforeItCommits()
    {
        await using var os = Start();
        var pay = os.Get<IPayments>(Pay);
        await pay.Charge(250m);
        await os.Idle();

        var e = await Assert.ThrowsAsync<RuleViolationException>(() => pay.Charge(100m));
        await os.Idle();

        Assert.Equal("weekly cap of $300", e.Rejected.Messages[0]);
        Assert.Single(os.Journal(Pay.Brain).Signals(Pay));
        var week = os.Journal(Pay.Brain).Signals(Week);
        Assert.Contains(week, s => s.Signal is StateChanged);
        Assert.Contains(week, s => s.Signal is Rejected);
    }

    [Fact]
    public async Task AReactionSynapseIsEnsuredAndDeliversOnce()
    {
        await using var os = Start();
        var pay = os.Get<IPayments>(Pay);
        await os.Activate(Books);
        await os.Idle();

        await pay.Charge(5m);
        await os.Idle();

        Assert.Equal(5m, await Spent(os));
        Assert.Contains(os.SynapsesOf(Books), s => s.From == Pay && s.Signal == typeof(Charging));

        await os.Restart();
        await os.Activate(Books);
        await os.Idle();

        Assert.Equal(5m, await Spent(os));
        Assert.Single(os.Journal(Books.Brain).Signals(Books), s => s.Signal is StateChanged);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task AGateThatNeverAnswersFailsClosed()
    {
        Stuck.Reset();
        await using var os = Start();
        var pay = os.Get<IPayments>(Pay);

        var call = pay.Stall();
        // The gate's wait starts a moment after the call: wait for it, then move past GateTimeout (still short of CallTimeout).
        for (var tries = 0; tries < 200 && !Stuck.Waiting.IsCompleted; tries++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        os.Clock.Advance(os.Options.GateTimeout + TimeSpan.FromSeconds(1));

        var e = await Assert.ThrowsAsync<RuleViolationException>(() => call);
        await os.Idle();

        Assert.Equal("the gate did not answer", e.Rejected.Messages[0]);
        Assert.Empty(os.Journal(Pay.Brain).Signals(Pay));
        Assert.Empty(os.Journal(Pay.Brain).Signals(NeuronId.Parse("b/hold/default")));
    }

    [Fact]
    public async Task AFailingCommittedOnAGateIsReportedAndDoesNotFailThePublisher()
    {
        await using var os = Start();

        await os.Get<IPayments>(Pay).Break();
        await os.Idle();

        Assert.Single(os.Journal(Pay.Brain).Signals(Pay), s => s.Signal is Fragile);
        Assert.Contains(os.Failures, e => e is InvalidOperationException { Message: "breaker" });
    }

    [Fact]
    public async Task ViewChangedIsOfferedAfterAnAllowedCharge()
    {
        await using var os = Start();
        var heard = new ConcurrentQueue<string>();
        await using var listener = await os.ClusterListeners.Listen<ViewChanged>("views", (_, changed, _) =>
        {
            heard.Enqueue(changed.Neuron);
            return Task.CompletedTask;
        });

        await os.Get<IPayments>(Pay).Charge(10m);
        await os.Idle();

        Assert.Contains("b/week/default", heard);
    }

    private static async Task<decimal> Spent(OS os)
    {
        var state = await ((IBrainObserver)os).State(Books, TestContext.Current.CancellationToken);
        return state!.Value.GetProperty("spent").GetDecimal();
    }
}
