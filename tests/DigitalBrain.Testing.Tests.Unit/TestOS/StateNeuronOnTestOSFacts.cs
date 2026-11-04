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

[Signal("test.synapse.lagging")]
public sealed record Lagging(decimal Amount) : Signal;

[State("lag")]
public sealed record LagSpend(decimal Spent)
{
    public LagSpend() : this(0m) { }
}

// A capped gate whose first call ignores cancellation: it answers Allowed only once released, after its publisher failed closed.
public static class Late
{
    private static TaskCompletionSource _release = New();
    private static TaskCompletionSource _waiting = New();
    private static int _calls;

    public static Task Waiting => _waiting.Task;

    public static void Reset()
    {
        _release = New();
        _waiting = New();
        _calls = 0;
    }

    public static void Release() => _release.TrySetResult();

    [Synapse(typeof(Lagging), "lag")]
    public static async Task<Check> Cap(Lagging s, LagSpend state, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _calls) == 1)
        {
            _waiting.TrySetResult();
            await _release.Task;
        }

        return state.Spent + s.Amount > 100m ? Check.Reject("cap of $100") : Check.Allow(state with { Spent = state.Spent + s.Amount });
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

[Signal("test.synapse.capped-charging")]
public sealed record CappedCharging(decimal Amount) : Signal;

[State("cap")]
public sealed record CapSpend(decimal Spent)
{
    public CapSpend() : this(0m) { }
}

public static class Capping
{
    [Synapse(typeof(CappedCharging), "cap")]
    public static Check Hundred(CappedCharging s, CapSpend state) =>
        state.Spent + s.Amount > 100m ? Check.Reject("cap of $100") : Check.Allow(state with { Spent = state.Spent + s.Amount });
}

public interface IPayments
{
    Task Charge(decimal amount);

    Task ChargeCapped(decimal amount);

    Task Stall();

    Task Lag(decimal amount);

    Task Break();
}

[Neuron("payments")]
public sealed class Payments : Neuron<PayState>, IPayments
{
    public Task Charge(decimal amount) => Save(State with { Count = State.Count + 1 }, new Charging(amount));

    public Task ChargeCapped(decimal amount) => Save(State with { Count = State.Count + 1 }, new CappedCharging(amount));

    public Task Stall() => Save(State with { Count = State.Count + 1 }, new Stalling());

    public Task Lag(decimal amount) => Save(State with { Count = State.Count + 1 }, new Lagging(amount));

    public Task Break() => Save(State with { Count = State.Count + 1 }, new Fragile());
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

    // Reaction edges and gates come from the install: each fact installs the aliases it charges.
    internal static async Task Install(OS os, string alias)
    {
        await os.Get<IInstalls>(NeuronId.Parse("b/synapse.installs/default")).Install(new(alias, new Dictionary<string, string>()), TestContext.Current.CancellationToken);
        await os.Idle();
    }

    [Fact]
    public async Task ASecondChargeOverTheCapIsRejectedBeforeItCommits()
    {
        await using var os = Start();
        await Install(os, "week");
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
    public async Task TwoConcurrentChargesAgainstTheCapAllowExactlyOne()
    {
        await using var os = Start();
        await Install(os, "cap");
        var first = os.Get<IPayments>(NeuronId.Parse("b/payments/one"));
        var second = os.Get<IPayments>(NeuronId.Parse("b/payments/two"));

        var charges = new[] { first.ChargeCapped(60m), second.ChargeCapped(60m) };
        await Assert.ThrowsAsync<RuleViolationException>(() => Task.WhenAll(charges));
        await os.Idle();

        Assert.Equal(1, charges.Count(c => c.IsCompletedSuccessfully));
        var state = await ((IBrainObserver)os).State(NeuronId.Parse("b/cap/default"), TestContext.Current.CancellationToken);
        Assert.Equal(60m, state!.Value.GetProperty("spent").GetDecimal());
    }

    [Fact]
    public async Task TwoConcurrentAllowedChargesCountBothWhateverOrderTheyCommitIn()
    {
        for (var i = 0; i < 20; i++)
        {
            await using var os = Start();
            await Install(os, "cap");
            var first = os.Get<IPayments>(NeuronId.Parse("b/payments/one"));
            var second = os.Get<IPayments>(NeuronId.Parse("b/payments/two"));

            await Task.WhenAll(first.ChargeCapped(40m), second.ChargeCapped(40m));
            await os.Idle();

            var state = await ((IBrainObserver)os).State(NeuronId.Parse("b/cap/default"), TestContext.Current.CancellationToken);
            Assert.Equal(80m, state!.Value.GetProperty("spent").GetDecimal());
        }
    }

    [Fact]
    public async Task AFirstCommitHeldUntilTheSecondCommittedFlushesBothOnce()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var os = Start(o => o.BeforeGateCommit = async (gate, _) =>
        {
            if (Interlocked.Increment(ref calls) != 1) { return; }
            entered.TrySetResult();
            await release.Task;
        });
        await Install(os, "cap");
        var first = os.Get<IPayments>(NeuronId.Parse("b/payments/one"));
        var second = os.Get<IPayments>(NeuronId.Parse("b/payments/two"));

        // The first publisher is gated first (the head) and its commit is held; the second is gated behind it and commits first.
        var held = first.ChargeCapped(40m);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await second.ChargeCapped(40m);
        release.TrySetResult();
        await held;
        await os.Idle();

        var cap = NeuronId.Parse("b/cap/default");
        var state = await ((IBrainObserver)os).State(cap, TestContext.Current.CancellationToken);
        Assert.Equal(80m, state!.Value.GetProperty("spent").GetDecimal());
        Assert.Single(os.Journal(cap.Brain).Signals(cap), s => s.Signal is StateChanged);
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
    public async Task AGateThatNeverAnswersFailsClosed()
    {
        Stuck.Reset();
        await using var os = Start();
        await Install(os, "hold");
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
    public async Task AGateThatAllowsAfterItsPublisherFailedClosedLeavesNoProvisionalEntry()
    {
        Late.Reset();
        await using var os = Start();
        await Install(os, "lag");
        var pay = os.Get<IPayments>(Pay);

        var call = pay.Lag(60m);
        for (var tries = 0; tries < 200 && !Late.Waiting.IsCompleted; tries++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        os.Clock.Advance(os.Options.GateTimeout + TimeSpan.FromSeconds(1));
        var e = await Assert.ThrowsAsync<RuleViolationException>(() => call);
        Assert.Equal("the gate did not answer", e.Rejected.Messages[0]);

        // The gate ignores the cancellation and answers Allowed(60) after the publisher gave up; the abort sent on timeout drops it.
        Late.Release();
        await os.Idle();

        // Against the inflated 60 this second 60 would break the cap of 100.
        await pay.Lag(60m);
        await os.Idle();

        var state = await ((IBrainObserver)os).State(NeuronId.Parse("b/lag/default"), TestContext.Current.CancellationToken);
        Assert.Equal(60m, state!.Value.GetProperty("spent").GetDecimal());
    }

    [Fact]
    public async Task AFailingCommittedOnAGateIsReportedAndDoesNotFailThePublisher()
    {
        await using var os = Start();
        await Install(os, "brittle");

        await os.Get<IPayments>(Pay).Break();
        await os.Idle();

        Assert.Single(os.Journal(Pay.Brain).Signals(Pay), s => s.Signal is Fragile);
        Assert.Contains(os.Failures, e => e is InvalidOperationException { Message: "breaker" });
    }

    [Fact]
    public async Task ViewChangedIsOfferedAfterAnAllowedCharge()
    {
        await using var os = Start();
        await Install(os, "week");
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
