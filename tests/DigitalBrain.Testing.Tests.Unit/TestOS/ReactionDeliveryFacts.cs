using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

[Signal("test.number")]
public sealed record Number(int Value) : Signal;

[Signal("test.other")]
public sealed record Other : Signal;

public interface INumberSource
{
    Task Emit(int value);

    Task EmitOther();
}

public interface INumberSink
{
    Task Subscribe(NeuronId source);
}

[Neuron("rx.source")]
public sealed class NumberSource : Neuron<Tally>, INumberSource
{
    public Task Emit(int value) => Save(State with { Total = value }, new Number(value));

    public Task EmitOther() => Save(State, new Other());
}

[Neuron("rx.closed")]
public sealed class ClosedSource : Neuron<Tally>, INumberSource
{
    public Task Emit(int value) => Save(State with { Total = value }, new Number(value));

    public Task EmitOther() => Save(State, new Other());

    protected override Task AdmitSynapse(NeuronId subscriber, IReadOnlyList<Type> signals)
        => throw new UnauthorizedAccessException($"{subscriber} is not admitted.");
}

public abstract class SinkBase(Probe probe) : Neuron, INumberSink
{
    public Task Subscribe(NeuronId source) => Context.Synapses.Ensure<Number>(source);

    [On]
    public Task On(Number number)
    {
        probe.Write($"n:{number.Value}");
        return Task.CompletedTask;
    }

    [On]
    public Task On(Other other)
    {
        probe.Write("other");
        return Task.CompletedTask;
    }

    [On]
    public Task On(Gap gap)
    {
        probe.Write($"gap:{gap.Source.Key}:{gap.From}-{gap.To}");
        return Task.CompletedTask;
    }
}

[Neuron("rx.sink")]
public sealed class NumberSink(Probe probe) : SinkBase(probe);

[Neuron("rx.stateless")]
[StatelessNeuron]
public sealed class StatelessSink(Probe probe) : SinkBase(probe);

// 04-08 Task 2: [On] delivery per the delivery contract: ordered per source, cursor dedupe, catch-up, Gap, admission.
public sealed class ReactionDeliveryFacts
{
    private static readonly BrainId Brain = new("ada");
    private static readonly NeuronId Source = new(Brain, "rx.source", "s");
    private static readonly NeuronId Sink = new(Brain, "rx.sink", "k");
    private readonly Probe _probe = new();

    private OS Start(Action<TestOSOptions>? configure = null)
    {
        var os = OS.Local(options =>
        {
            options.Services = _probe;
            configure?.Invoke(options);
        });
        return os.Register(typeof(NumberSink).Assembly);
    }

    private static async Task Emit(OS os, params int[] values)
    {
        foreach (var value in values) { await os.Get<INumberSource>(Source).Emit(value); }
        await os.Idle();
    }

    // Behaviour 1
    [Fact]
    public async Task Every_signal_the_source_saves_reaches_the_reaction_once_in_sequence_order()
    {
        await using var os = Start();
        await os.Get<INumberSink>(Sink).Subscribe(Source);

        await Emit(os, 1, 2, 3);

        Assert.Equal(["n:1", "n:2", "n:3"], _probe.Log);
        Assert.Equal(3, os.Cursor(Sink, Source));
        Assert.Equal([new Synapse(Source, typeof(Number), Sink)], os.SynapsesOf(Sink));
        Assert.Empty(os.Failures);
    }

    // Behaviour 2
    [Fact]
    public async Task Signals_of_other_types_and_sources_without_a_synapse_are_not_delivered()
    {
        await using var os = Start();
        await os.Get<INumberSink>(Sink).Subscribe(Source);

        await os.Get<INumberSource>(Source).EmitOther();
        await os.Get<INumberSource>(new NeuronId(Brain, "rx.source", "elsewhere")).Emit(7);
        await Emit(os, 1);

        Assert.Equal(["n:1"], _probe.Log);
        Assert.Equal(2, os.Cursor(Sink, Source));
    }

    // Behaviour 3
    [Fact]
    public async Task Duplicate_references_reach_the_reaction_once()
    {
        await using var os = Start(options => options.DuplicateDelivery = true);
        await os.Get<INumberSink>(Sink).Subscribe(Source);

        await Emit(os, 1, 2, 3);

        Assert.Equal(["n:1", "n:2", "n:3"], _probe.Log);
        Assert.True(os.DuplicatesDropped >= 3, $"dropped {os.DuplicatesDropped}");
    }

    // Behaviour 4
    [Fact]
    public async Task A_reactivated_subscriber_catches_up_once_in_order()
    {
        await using var os = Start();
        await os.Get<INumberSink>(Sink).Subscribe(Source);
        await os.Deactivate(Sink);

        await Emit(os, 1, 2, 3);
        Assert.Empty(_probe.Log);

        await os.Activate(Sink);
        await os.Idle();
        await os.Activate(Sink);
        await os.Idle();

        Assert.Equal(["n:1", "n:2", "n:3"], _probe.Log);
        Assert.Equal(3, os.Cursor(Sink, Source));
    }

    // Behaviour 5
    [Fact]
    public async Task A_stale_cursor_behind_a_forced_checkpoint_receives_Gap_then_the_signals_after_the_floor()
    {
        await using var os = Start(options =>
        {
            options.HistoryWindowSignals = 2;
            options.HistoryWindowAge = TimeSpan.FromMinutes(1);
            options.StaleCursorAfter = TimeSpan.FromMinutes(1);
        });
        await os.Get<INumberSink>(Sink).Subscribe(Source);
        await Emit(os, 1);
        await os.Deactivate(Sink);

        await Emit(os, 2, 3, 4, 5, 6);
        os.Clock.Advance(TimeSpan.FromMinutes(2));
        os.ForceCheckpoint(Brain);
        Assert.Equal(4, os.Journal(Brain).Floor(Source));

        await os.Activate(Sink);
        await os.Idle();

        Assert.Equal(["n:1", "gap:s:1-4", "n:5", "n:6"], _probe.Log);
        Assert.Equal(6, os.Cursor(Sink, Source));
    }

    [Fact]
    public async Task A_live_cursor_pins_the_floor()
    {
        await using var os = Start(options =>
        {
            options.HistoryWindowSignals = 2;
            options.HistoryWindowAge = TimeSpan.FromMinutes(1);
            options.StaleCursorAfter = TimeSpan.FromMinutes(10);
        });
        await os.Get<INumberSink>(Sink).Subscribe(Source);
        await Emit(os, 1);
        await os.Deactivate(Sink);
        await Emit(os, 2, 3, 4, 5, 6);
        os.Clock.Advance(TimeSpan.FromMinutes(2));

        os.ForceCheckpoint(Brain);
        await os.Activate(Sink);
        await os.Idle();

        Assert.Equal(1, os.Journal(Brain).Floor(Source));
        Assert.Equal(["n:1", "n:2", "n:3", "n:4", "n:5", "n:6"], _probe.Log);
    }

    // Behaviour 6
    [Fact]
    public async Task History_Read_returns_journal_pages_with_Floor_and_Last()
    {
        await using var os = Start(options =>
        {
            options.HistoryWindowSignals = 2;
            options.HistoryWindowAge = TimeSpan.FromMinutes(1);
        });
        await Emit(os, 1, 2, 3, 4);
        var history = os.Journal(Brain);

        var first = await history.Read(Source, 0, 2, CancellationToken.None);
        Assert.Equal((0L, 4L), (first.Floor, first.Last));
        Assert.Equal([1L, 2L], first.Signals.Select(entry => entry.Sequence));
        Assert.Equal(new Number(1), first.Signals[0].Signal);

        os.Clock.Advance(TimeSpan.FromMinutes(2));
        os.ForceCheckpoint(Brain);
        var trimmed = await history.Read(Source, 0, 10, CancellationToken.None);
        Assert.Equal((2L, 4L), (trimmed.Floor, trimmed.Last));
        Assert.Equal([3L, 4L], trimmed.Signals.Select(entry => entry.Sequence));

        var brain = await history.Read(0, 10, CancellationToken.None);
        Assert.Equal(4L, brain.Last);
        Assert.Equal([new Number(3), new Number(4)], brain.Signals.Select(entry => entry.Signal));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => history.Read(Source, 0, 0, CancellationToken.None));
    }

    // Behaviour 7
    [Fact]
    public async Task A_source_that_refuses_the_subscriber_makes_Ensure_throw_and_creates_no_synapse()
    {
        await using var os = Start();
        var closed = new NeuronId(Brain, "rx.closed", "c");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => os.Get<INumberSink>(Sink).Subscribe(closed));
        await os.Get<INumberSource>(closed).Emit(1);
        await os.Idle();

        Assert.Empty(os.SynapsesOf(Sink));
        Assert.Empty(_probe.Log);
    }

    [Fact]
    public async Task Ensure_throws_on_a_StatelessNeuron()
    {
        await using var os = Start();
        var stateless = new NeuronId(Brain, "rx.stateless", "x");

        await Assert.ThrowsAsync<InvalidOperationException>(() => os.Get<INumberSink>(stateless).Subscribe(Source));
        Assert.Empty(os.SynapsesOf(stateless));
    }
}
