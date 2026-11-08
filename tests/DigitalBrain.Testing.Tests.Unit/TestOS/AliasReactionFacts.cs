using DigitalBrain.Spi;
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
}
