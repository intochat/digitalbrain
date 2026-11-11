using DigitalBrain.Kernel;
using DigitalBrain.UI;
using DigitalBrain.Neurons;
using DigitalBrain.Tests.Unit.Neurons;
using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.Synapses;

[State("ledger")]
public sealed record Ledger(decimal Spent)
{
    public Ledger() : this(0m) { }
}

public static class Books
{
    [Synapse(typeof(Charging), "ledger")]
    public static Ledger Add(Charging s, Ledger w) => w with { Spent = w.Spent + s.Amount };

    [Synapse("ledger")]
    public static View Show(Ledger w) => new("ledger", new Text($"{w.Spent} of 300"));
}

public static class Idle
{
    [Synapse(typeof(Charging), "ledger")]
    public static Ledger Keep(Charging s, Ledger w) => w;
}

public static class Shaky
{
    [Synapse(typeof(Charging), "ledger")]
    public static Ledger Boom(Charging s, Ledger w) => throw new InvalidOperationException(new string('x', 600));
}

public static class Echoing
{
    [Synapse(typeof(SynapseFailed), "ledger")]
    public static Ledger Again(SynapseFailed s, Ledger w) => throw new InvalidOperationException("again");
}

[State("blob")]
public sealed record BlobState(string Data)
{
    public BlobState() : this("") { }
}

public static class BlobSynapses
{
    [Synapse(typeof(Charging), "blob")]
    public static BlobState Inflate(Charging s, BlobState state) =>
        new(new string('x', (int)s.Amount));
}

// TrackerStateV1 simulates an earlier schema version payload; no [State] attribute to avoid duplicate state alias in assembly scan
public sealed record TrackerStateV1(int Count)
{
    public TrackerStateV1() : this(0) { }
}

[State("evolve.tracker")]
public sealed record TrackerStateV2(int Count, string Label = "upgraded")
{
    public TrackerStateV2() : this(0, "upgraded") { }
}

public static class TrackerSynapsesV2
{
    [Synapse(typeof(Charging), "evolve.tracker")]
    public static TrackerStateV2 Bump(Charging s, TrackerStateV2 state) =>
        state with { Count = state.Count + (int)s.Amount, Label = string.IsNullOrEmpty(state.Label) ? "upgraded" : state.Label };
}

public sealed class StateNeuronFacts
{
    private static readonly NeuronId Id = new(new BrainId("alice"), "ledger", "default");

    private RecordingContext Context { get; } = new(Id);

    private static SynapseCatalog Catalog(params Type[] types) => SynapseCatalog.Scan(types);

    private static SynapseCatalogSource Source(params Type[] types) => new(Catalog(types));

    private UnitNeuron<TState> Attach<TState>(UnitNeuron<TState> neuron) where TState : class, new()
    {
        NeuronAttachment.Attach(neuron, Context);
        return neuron;
    }

    [Fact]
    public async Task ReceiveRunsTheReactionAndThenTheViews()
    {
        var n = Attach(new UnitNeuron<Ledger>(Source(typeof(Ledger), typeof(Books)), new UnboundSlotResolver()));

        await n.Receive(new Charging(5m), TestContext.Current.CancellationToken);

        Assert.Equal(5m, ((Ledger)Context.Writes[0].State).Spent);
        Assert.Equal([new StateChanged(Id.ToString(), "test.charging"), new ViewChanged("ledger", 1)], Context.Writes[0].Signals);
        var e = Context.Published;
        var started = e.FindIndex(p => p is SynapseStarted s && s.Function.EndsWith(".Add", StringComparison.Ordinal));
        var finished = e.FindIndex(p => p is SynapseFinished { Outcome: "saved" } f && f.Function.EndsWith(".Add", StringComparison.Ordinal));
        var viewed = e.FindIndex(p => p is SynapseFinished { Outcome: "viewed" });
        Assert.True(started >= 0 && finished > started && viewed > finished, string.Join(", ", e));
        Assert.Contains(e, p => p is SynapseFinished { Outcome: "viewed" });
    }

    [Fact]
    public async Task ReceiveWithUnchangedStateSavesNothing()
    {
        var n = Attach(new UnitNeuron<Ledger>(Source(typeof(Ledger), typeof(Idle)), new UnboundSlotResolver()));

        await n.Receive(new Charging(5m), TestContext.Current.CancellationToken);

        Assert.Empty(Context.Writes);
        Assert.Contains(Context.Published, p => p is SynapseFinished { Outcome: "unchanged" });
    }

    [Fact]
    public async Task AThrowingReactionJournalsOneSynapseFailedAndRethrows()
    {
        var n = Attach(new UnitNeuron<Ledger>(Source(typeof(Ledger), typeof(Shaky)), new UnboundSlotResolver()));
        var arriving = new JournaledSignal(NeuronId.Parse("alice/pay/one"), 7, new Charging(5m), DateTimeOffset.UnixEpoch);

        await Assert.ThrowsAsync<InvalidOperationException>(() => n.Receive(arriving, TestContext.Current.CancellationToken));

        var failed = Assert.Single(Context.Published.OfType<SynapseFailed>());
        Assert.Equal("ledger", failed.Alias);
        Assert.EndsWith(".Boom", failed.Function, StringComparison.Ordinal);
        Assert.Equal(SignalAliases.Of(arriving.Signal), failed.Cause);
        Assert.Equal(Causation.Address(arriving.Publisher, 7), failed.CauseId);
        Assert.Equal("InvalidOperationException", failed.Error);
        Assert.Equal(500, failed.Message.Length);
        Assert.Empty(Context.Writes);
        Assert.Contains(Context.Published, p => p is SynapseFinished { Outcome: "failed" });
    }

    [Fact]
    public async Task AFailureWhileReactingToSynapseFailedIsNotJournaled()
    {
        var n = Attach(new UnitNeuron<Ledger>(Source(typeof(Ledger), typeof(Echoing)), new UnboundSlotResolver()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => n.Receive(new SynapseFailed("other", "X.Y", "time.tick", null, "E", "m"), TestContext.Current.CancellationToken));

        Assert.Empty(Context.Published.OfType<SynapseFailed>());
    }

    [Fact]
    public async Task StateExceedingMaxStateBytesIsRefusedBeforeSaving()
    {
        var n = Attach(new UnitNeuron<BlobState>(Source(typeof(BlobState), typeof(BlobSynapses)), new UnboundSlotResolver()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => n.Receive(new Charging(UnitLimits.MaxStateBytes + 128), TestContext.Current.CancellationToken));

        Assert.Contains("exceeding the 2097152-byte cap", ex.Message, StringComparison.Ordinal);
        Assert.Empty(Context.Writes);
    }

    [Fact]
    public void UnitNeuronExposesResourceGovernanceLimits()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), UnitLimits.ReactionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), UnitLimits.SlotReactionTimeout);
        Assert.Equal(2 * 1024 * 1024, UnitLimits.MaxStateBytes);
    }

    [Fact]
    public async Task StoredStateSurvivesAdditiveDay2SchemaEvolutionAcrossUnitUpgrade()
    {
        var v1Bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new StoredUnit<TrackerStateV1>(new TrackerStateV1(7), null),
            DigitalBrain.Invocation.RuntimeProtocol.Json);
        Context.Store(v1Bytes);

        var n = Attach(new UnitNeuron<TrackerStateV2>(Source(typeof(TrackerStateV2), typeof(TrackerSynapsesV2)), new UnboundSlotResolver()));
        await NeuronAttachment.Activate(n, TestContext.Current.CancellationToken);
        await n.Receive(new Charging(3m), TestContext.Current.CancellationToken);

        var saved = Assert.IsType<TrackerStateV2>(Assert.Single(Context.Writes).State);
        Assert.Equal(10, saved.Count);
        Assert.Equal("upgraded", saved.Label);
    }
}
