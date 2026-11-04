using DigitalBrain.Rules;
using DigitalBrain.Spi;
using DigitalBrain.Tests.Unit.Neurons;

namespace DigitalBrain.Tests.Unit.Synapses;

[State("ledger")]
public sealed record Ledger(decimal Spent)
{
    public Ledger() : this(0m) { }
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

    [Synapse("ledger")]
    public static string Show(Ledger w) => $"{w.Spent} of 300";
}

public static class Idle
{
    [Synapse(typeof(Charging), "ledger")]
    public static Ledger Keep(Charging s, Ledger w) => w;
}

public static class Hundred
{
    [Synapse(typeof(Charging), "week")]
    public static Check Cap(Charging s, WeekSpend state) =>
        state.Spent + s.Amount > 100m ? Check.Reject("cap of $100") : Check.Allow(state with { Spent = state.Spent + s.Amount });
}

public static class Faulty
{
    [Synapse(typeof(Charging), "week")]
    public static Check Broken(Charging s, WeekSpend w) => throw new InvalidOperationException("broken");
}

[State("balance")]
public sealed record Balance(decimal Amount)
{
    public Balance() : this(0m) { }
}

[Signal("test.deposit")]
public sealed record Deposit(decimal Amount) : Signal;

[Signal("test.withdraw")]
public sealed record Withdraw(decimal Amount) : Signal;

public static class Account
{
    [Synapse(typeof(Deposit), "balance")]
    public static Check Credit(Deposit s, Balance b) => Check.Allow(b with { Amount = b.Amount + s.Amount });

    [Synapse(typeof(Withdraw), "balance")]
    public static Check Debit(Withdraw s, Balance b) =>
        b.Amount - s.Amount < 0m ? Check.Reject("no overdraft") : Check.Allow(b with { Amount = b.Amount - s.Amount });
}

[Signal("test.probe")]
public sealed record Probe : Signal;

public static class Fragile
{
    [Synapse(typeof(Probe), "week")]
    public static Check OnlyAboveZero(Probe s, WeekSpend w) =>
        w.Spent == 0m ? throw new InvalidOperationException("zero base") : Check.Allow(w);
}

public sealed class StateNeuronFacts
{
    private static readonly NeuronId Id = new(new BrainId("alice"), "week", "default");

    private RecordingContext Context { get; } = new(Id);

    private static SynapseCatalog Catalog(params Type[] types) => SynapseCatalog.Scan(types);

    private StateNeuron<TState> Attach<TState>(StateNeuron<TState> neuron) where TState : class, new()
    {
        NeuronAttachment.Attach(neuron, Context);
        return neuron;
    }

    [Fact]
    public async Task GateUnderCapAllowsAndSavesOnlyAfterCommitted()
    {
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Budget))));
        var c = await n.Gate(new Charging(100m), TestContext.Current.CancellationToken);
        var allowed = Assert.IsType<Check.Allowed>(c);
        Assert.Empty(Context.Writes);

        await n.Committed(new Charging(100m), allowed);

        Assert.Equal(100m, ((WeekSpend)Context.Writes[0].State).Spent);
        Assert.Equal([new StateChanged(Id.ToString(), "test.charging")], Context.Writes[0].Signals);
        Assert.Contains(Context.Published, p => p is ViewChanged { View: "100 of 300" });
    }

    [Fact]
    public async Task CommittedJournalsStateChangedNotTheTriggeringSignal()
    {
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Budget))));
        var allowed = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(10m), TestContext.Current.CancellationToken));

        await n.Committed(new Charging(10m), allowed);

        var changed = Assert.IsType<StateChanged>(Assert.Single(Assert.Single(Context.Writes).Signals));
        Assert.Equal(("alice/week/default", "test.charging"), (changed.Neuron, changed.Cause));
    }

    [Fact]
    public async Task GateOverCapRefusesAndJournalsRejectedNamedAfterTheFunction()
    {
        var seeded = new WeekSpend(default, 250m);
        Context.Store(seeded);
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Budget))));
        await NeuronAttachment.Activate(n, TestContext.Current.CancellationToken);

        var c = await n.Gate(new Charging(100m), TestContext.Current.CancellationToken);

        Assert.IsType<Check.Refused>(c);
        var write = Assert.Single(Context.Writes);
        Assert.Equal(seeded, write.State);
        var rejected = Assert.IsType<Rejected>(Assert.Single(write.Signals));
        Assert.EndsWith(".Budget.WeeklyCap", rejected.Rules[0], StringComparison.Ordinal);
        Assert.Equal("weekly cap of $300", rejected.Messages[0]);
        Assert.Contains(Context.Published, p => p is SynapseFinished { Outcome: "rejected" });
    }

    [Fact]
    public async Task ReceiveRunsTheReactionAndThenTheViews()
    {
        var n = Attach(new StateNeuron<Ledger>(Catalog(typeof(Ledger), typeof(Books))));

        await n.Receive(new Charging(5m), TestContext.Current.CancellationToken);

        Assert.Equal(5m, ((Ledger)Context.Writes[0].State).Spent);
        Assert.Equal([new StateChanged(Id.ToString(), "test.charging")], Context.Writes[0].Signals);
        var e = Context.Published;
        var started = e.FindIndex(p => p is SynapseStarted s && s.Function.EndsWith(".Add", StringComparison.Ordinal));
        var finished = e.FindIndex(p => p is SynapseFinished { Outcome: "saved" } f && f.Function.EndsWith(".Add", StringComparison.Ordinal));
        var viewed = e.FindIndex(p => p is ViewChanged { View: "5 of 300" });
        Assert.True(started >= 0 && finished > started && viewed > finished, string.Join(", ", e));
        Assert.Contains(e, p => p is SynapseFinished { Outcome: "viewed" });
    }

    [Fact]
    public async Task ReceiveWithUnchangedStateSavesNothing()
    {
        var n = Attach(new StateNeuron<Ledger>(Catalog(typeof(Ledger), typeof(Idle))));

        await n.Receive(new Charging(5m), TestContext.Current.CancellationToken);

        Assert.Empty(Context.Writes);
        Assert.Contains(Context.Published, p => p is SynapseFinished { Outcome: "unchanged" });
    }

    [Fact]
    public async Task AThrowingGateReportsFailedAndRethrows()
    {
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Faulty))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => n.Gate(new Charging(1m), TestContext.Current.CancellationToken));

        Assert.Empty(Context.Writes);
        Assert.Contains(Context.Published, p => p is SynapseFinished { Outcome: "failed" } f && f.Function.EndsWith(".Broken", StringComparison.Ordinal));
    }

    [Fact]
    public void CatalogGateIndexNamesTheGateNeuron()
    {
        var index = new CatalogGateIndex(Catalog(typeof(WeekSpend), typeof(Budget)));

        var (gate, synapse) = Assert.Single(index.GatesFor(new BrainId("alice"), typeof(Charging)));

        Assert.Equal(NeuronId.Parse("alice/week/default"), gate);
        Assert.Equal(nameof(Budget.WeeklyCap), synapse.Function.Name);
    }

    [Fact]
    public async Task ASecondGateSeesTheProvisionalStateOfTheFirstAndRefuses()
    {
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Hundred))));

        var first = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(60m), TestContext.Current.CancellationToken));
        var second = await n.Gate(new Charging(60m), TestContext.Current.CancellationToken);

        Assert.IsType<Check.Refused>(second);
        Assert.Equal(0m, Assert.IsType<WeekSpend>(Assert.Single(Context.Writes).State).Spent);
        await n.Committed(new Charging(60m), first);
        Assert.Equal(60m, ((WeekSpend)Context.Writes[^1].State).Spent);
    }

    [Fact]
    public async Task CommittedSavesTheEntryAndKeepsTheLaterProvisionalEntries()
    {
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Hundred))));
        var ten = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(10m), TestContext.Current.CancellationToken));
        Assert.IsType<Check.Allowed>(await n.Gate(new Charging(20m), TestContext.Current.CancellationToken));

        await n.Committed(new Charging(10m), ten);

        Assert.Equal(10m, ((WeekSpend)Assert.Single(Context.Writes).State).Spent);
        var seventy = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(70m), TestContext.Current.CancellationToken));
        Assert.Equal(100m, ((WeekSpend)seventy.Next).Spent);
        Assert.IsType<Check.Refused>(await n.Gate(new Charging(1m), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AbortedDropsTheEntryWithoutSavingAndKeepsTheLaterEntries()
    {
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Hundred))));
        Assert.IsType<Check.Allowed>(await n.Gate(new Charging(60m), TestContext.Current.CancellationToken));
        Assert.IsType<Check.Allowed>(await n.Gate(new Charging(30m), TestContext.Current.CancellationToken));

        await n.Aborted(new Charging(60m));

        Assert.Empty(Context.Writes);
        var again = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(60m), TestContext.Current.CancellationToken));
        Assert.Equal(90m, ((WeekSpend)again.Next).Spent);
    }

    [Fact]
    public async Task AProvisionalEntryOlderThanTheLeaseIsDiscardedOnTheNextGate()
    {
        var clock = new SteppedClock();
        Context.Time = clock;
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Hundred))));
        Assert.IsType<Check.Allowed>(await n.Gate(new Charging(60m), TestContext.Current.CancellationToken));

        clock.Now += StateNeuron.ProvisionalLease + TimeSpan.FromSeconds(1);

        var later = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(60m), TestContext.Current.CancellationToken));
        Assert.Equal(60m, ((WeekSpend)later.Next).Spent);
        Assert.Equal(TimeSpan.FromSeconds(30), StateNeuron.ProvisionalLease);
    }

    [Fact]
    public async Task ALaterCommitBeforeTheHeadSavesNothingUntilTheHeadCommits()
    {
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Hundred))));
        var a = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(40m), TestContext.Current.CancellationToken));
        var b = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(41m), TestContext.Current.CancellationToken));
        Assert.Equal(81m, ((WeekSpend)b.Next).Spent);

        await n.Committed(new Charging(41m), b);
        Assert.Empty(Context.Writes);

        await n.Committed(new Charging(40m), a);
        Assert.Equal(81m, ((WeekSpend)Assert.Single(Context.Writes).State).Spent);
        var after = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(19m), TestContext.Current.CancellationToken));
        Assert.Equal(19m, ((WeekSpend)after.Next).Spent - 81m);
    }

    [Fact]
    public async Task AbortedReGatesTheLaterEntriesOnTheNewBase()
    {
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Hundred))));
        Assert.IsType<Check.Allowed>(await n.Gate(new Charging(40m), TestContext.Current.CancellationToken));
        var b = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(41m), TestContext.Current.CancellationToken));
        Assert.IsType<Check.Refused>(await n.Gate(new Charging(42m), TestContext.Current.CancellationToken));
        var writes = Context.Writes.Count;

        await n.Aborted(new Charging(40m));
        Assert.Equal(writes, Context.Writes.Count);

        await n.Committed(new Charging(41m), b);
        Assert.Equal(41m, ((WeekSpend)Context.Writes[^1].State).Spent);
    }

    [Fact]
    public async Task AReGateThatRefusesIsALateRefusalAndCommitsTheBase()
    {
        Context.Store(new Balance(100m));
        var n = Attach(new StateNeuron<Balance>(Catalog(typeof(Balance), typeof(Account))));
        await NeuronAttachment.Activate(n, TestContext.Current.CancellationToken);
        Assert.IsType<Check.Allowed>(await n.Gate(new Deposit(50m), TestContext.Current.CancellationToken));
        var b = Assert.IsType<Check.Allowed>(await n.Gate(new Withdraw(120m), TestContext.Current.CancellationToken));
        Assert.Equal(30m, ((Balance)b.Next).Amount);

        await n.Aborted(new Deposit(50m));

        Assert.Empty(Context.Writes);
        var late = Assert.IsType<Rejected>(Assert.Single(Context.Published, p => p is Rejected));
        Assert.EndsWith(".Account.Debit (late)", late.Operation, StringComparison.Ordinal);
        Assert.Equal("no overdraft", late.Messages[0]);
        await n.Committed(new Withdraw(120m), b);
        Assert.Equal(100m, ((Balance)Assert.Single(Context.Writes).State).Amount);
    }

    [Fact]
    public async Task EqualSignalsCommitDistinctEntriesAndTheHeadFlushesAllThree()
    {
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Hundred))));
        var head = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(10m), TestContext.Current.CancellationToken));
        var twin = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(40m), TestContext.Current.CancellationToken));
        Assert.IsType<Check.Allowed>(await n.Gate(new Charging(40m), TestContext.Current.CancellationToken));

        await n.Committed(new Charging(40m), twin);
        await n.Committed(new Charging(40m), twin);
        Assert.Empty(Context.Writes);
        await n.Committed(new Charging(10m), head);

        Assert.Equal(90m, ((WeekSpend)Assert.Single(Context.Writes).State).Spent);
    }

    [Fact]
    public async Task AnExpiredUncommittedHeadIsPrunedAndTheCommittedEntryBehindItIsSavedOnce()
    {
        var clock = new SteppedClock();
        Context.Time = clock;
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Hundred))));
        Assert.IsType<Check.Allowed>(await n.Gate(new Charging(10m), TestContext.Current.CancellationToken));
        clock.Now += TimeSpan.FromSeconds(20);
        var b = Assert.IsType<Check.Allowed>(await n.Gate(new Charging(20m), TestContext.Current.CancellationToken));
        await n.Committed(new Charging(20m), b);
        Assert.Empty(Context.Writes);

        clock.Now += TimeSpan.FromSeconds(15);
        await n.Gate(new Charging(1m), TestContext.Current.CancellationToken);

        Assert.Equal(30m, ((WeekSpend)Assert.Single(Context.Writes).State).Spent);
    }

    [Fact]
    public async Task AReGateThatThrowsIsALateRefusalAndAbortedCompletes()
    {
        var n = Attach(new StateNeuron<WeekSpend>(Catalog(typeof(WeekSpend), typeof(Hundred), typeof(Fragile))));
        Assert.IsType<Check.Allowed>(await n.Gate(new Charging(10m), TestContext.Current.CancellationToken));
        var probe = Assert.IsType<Check.Allowed>(await n.Gate(new Probe(), TestContext.Current.CancellationToken));

        await n.Aborted(new Charging(10m));

        var late = Assert.IsType<Rejected>(Assert.Single(Context.Published, p => p is Rejected));
        Assert.EndsWith(".Fragile.OnlyAboveZero (late)", late.Operation, StringComparison.Ordinal);
        Assert.Equal("zero base", late.Messages[0]);
        await n.Committed(new Probe(), probe);
        Assert.Equal(0m, ((WeekSpend)Assert.Single(Context.Writes).State).Spent);
    }

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
