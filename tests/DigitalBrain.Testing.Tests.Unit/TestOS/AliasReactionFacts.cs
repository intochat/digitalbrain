using DigitalBrain.Neurons;
using DigitalBrain.Units;
using DigitalBrain.Kernel;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Synapses;

// Unit A: a neuron that publishes Polled. Unit B (below) names the signal only by its alias.
[Signal("x.polled")]
public sealed record Polled(int Count) : Signal;

public sealed record PollerState
{
    public int Polls { get; init; }
}

public interface IPoller
{
    Task Poll(int count);
}

[Neuron("poller")]
public sealed class Poller : Neuron<PollerState>, IPoller
{
    public Task Poll(int count) => Save(State with { Polls = State.Polls + 1 }, new Polled(count));
}

[State("dash")]
public sealed record DashBoard(int Count, string? TraceId, string? Alias)
{
    public DashBoard() : this(0, null, null) { }
}

public static class Dash
{
    [Synapse("x.polled", "dash")]
    public static DashBoard Seen(SignalEnvelope s, DashBoard d) => new(s.Payload.GetProperty("count").GetInt32(), s.TraceId, s.Alias);
}

// A payload that cannot be serialised: its getter throws, so the alias reaction's envelope cannot be built.
[Signal("x.unserialisable")]
public sealed record Unserialisable(int Count) : Signal
{
    public int Broken => Count < 0 ? Count : throw new InvalidOperationException("no payload");
}

public interface IBadPoller
{
    Task Poll();
}

[Neuron("bad.poller")]
public sealed class BadPoller : Neuron<PollerState>, IBadPoller
{
    public Task Poll() => Save(State with { Polls = State.Polls + 1 }, new Unserialisable(1));
}

[State("bad.dash")]
public sealed record BadDash(int Count)
{
    public BadDash() : this(0) { }
}

public static class BadDashReactions
{
    [Synapse("x.unserialisable", "bad.dash")]
    public static BadDash Seen(SignalEnvelope s, BadDash d) => d with { Count = d.Count + 1 };
}

public sealed class AliasReactionFacts
{
    private static readonly NeuronId Installs = NeuronId.Parse("ar/synapse.installs/default");
    private static readonly NeuronId Board = NeuronId.Parse("ar/dash/default");
    private static readonly NeuronId Pollster = NeuronId.Parse("ar/poller/a");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AReactionByAliasReceivesTheEnvelope()
    {
        await using var os = OS.Local().Register(typeof(StateNeuronOnTestOSFacts).Assembly);
        os.Confirmations.ApproveAll();
        var outcome = await os.Get<IInstalls>(Installs).Install(new InstallRequest("dash", new Dictionary<string, string>()), Ct);
        Assert.Equal(InstallStatus.Pending, outcome.Status);
        await os.Idle();

        await os.Get<IPoller>(Pollster).Poll(3);
        await os.Idle();

        var published = Assert.Single(os.Journal(Pollster.Brain).Signals(Pollster), item => item.Signal is Polled);
        var state = await ((IBrainObserver)os).State(Board, Ct);
        Assert.Equal(3, state?.GetProperty("count").GetInt32());
        Assert.Equal("x.polled", state?.GetProperty("alias").GetString());
        Assert.NotNull(published.TraceId);
        Assert.Equal(published.TraceId, state?.GetProperty("traceId").GetString());
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task SeededStoredStateIsLoadedOnActivation()
    {
        await using var os = OS.Local().Register(typeof(StateNeuronOnTestOSFacts).Assembly);
        var poller = NeuronId.Parse("seed/poller/a");
        os.SeedStoredState(poller, """{"polls":5}""");

        await os.Get<IPoller>(poller).Poll(1);
        await os.Idle();

        var state = await ((IBrainObserver)os).State(poller, Ct);
        Assert.Equal(6, state?.GetProperty("polls").GetInt32());
    }

    [Fact]
    public async Task AnEnvelopeThatCannotBeBuiltFinishesTheSynapseAsFailed()
    {
        await using var os = OS.Local().Register(typeof(StateNeuronOnTestOSFacts).Assembly);
        os.Confirmations.ApproveAll();
        var finished = new System.Collections.Concurrent.ConcurrentQueue<SynapseFinished>();
        await using var listener = await os.ClusterListeners.Listen<SynapseFinished>("bad", (_, signal, _) =>
        {
            finished.Enqueue(signal);
            return Task.CompletedTask;
        });
        _ = await os.Get<IInstalls>(Installs).Install(new InstallRequest("bad.dash", new Dictionary<string, string>()), Ct);
        await os.Idle();

        try { await os.Get<IBadPoller>(NeuronId.Parse("ar/bad.poller/a")).Poll(); } catch (InvalidOperationException) { }
        await os.Idle();

        Assert.Contains(finished, f => f.Outcome == "failed" && f.Function.EndsWith(".Seen", StringComparison.Ordinal) && f.Neuron == "ar/bad.dash/default");
    }
}
