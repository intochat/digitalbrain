using System.Collections.Concurrent;
using DigitalBrain.Spi;

namespace DigitalBrain.Framework.Tests.E2E;

// Plain neurons: no Orleans namespace, no grain base. TestOS and the Orleans host grains run the same classes.

/// <summary>The module the Orleans targets host; TestOS registers the same assembly.</summary>
[Module("framework-e2e")]
public sealed class FrameworkModule : Module
{
    public static ModuleManifest Manifest { get; } = new([], [], [], [], []);
}

[Signal("e2e.number")]
public sealed record Number(int Value) : Signal;

/// <summary>What a listener reacted to; every state change publishes at least one signal.</summary>
[Signal("e2e.reacted")]
public sealed record Reacted(string Item) : Signal;

public sealed record SourceState
{
    public int Count { get; init; }
}

public interface ISource
{
    Task Emit(int value);

    Task<int> Count();
}

/// <summary>Saves one <see cref="Number"/> per call with its state change.</summary>
[Neuron("e2e.source", State = "source")]
public sealed class SourceNeuron : Neuron<SourceState>, ISource
{
    public Task Emit(int value) => Save(State with { Count = State.Count + 1 }, [new Number(value)]);

    public Task<int> Count() => Task.FromResult(State.Count);
}

public sealed record ListenerState
{
    public IReadOnlyList<string> Heard { get; init; } = [];
}

public interface IListener
{
    Task Follow(string source);

    Task<string[]> Heard();
}

/// <summary>Holds a synapse on a source and records each reaction in its own state with <c>Save(next, signals)</c>.</summary>
[Neuron("e2e.listener", State = "heard")]
public sealed class ListenerNeuron : Neuron<ListenerState>, IListener
{
    public Task Follow(string source) => Context.Synapses.Ensure<Number>(NeuronId.Parse(source));

    public Task<string[]> Heard() => Task.FromResult(State.Heard.ToArray());

    [On]
    public Task On(Number signal) => Record("n:" + signal.Value);

    [On]
    public Task On(Gap gap) => Record($"gap:{gap.From}-{gap.To}");

    private Task Record(string item) => Save(State with { Heard = [.. State.Heard, item] }, [new Reacted(item)]);
}

/// <summary>What the turn probes saw, keyed by neuron id; every fact uses a fresh brain.</summary>
internal static class Turns
{
    private static readonly ConcurrentDictionary<string, int> Running = new();
    private static readonly ConcurrentDictionary<string, int> Most = new();
    private static readonly ConcurrentDictionary<string, int> Met = new();
    private static readonly ConcurrentDictionary<string, int> Ticked = new();

    public static int MostConcurrent(string id) => Most.GetValueOrDefault(id);

    public static int Ticks(string id) => Ticked.GetValueOrDefault(id);

    public static async Task Inside(string id, TimeSpan hold)
    {
        var now = Running.AddOrUpdate(id, 1, static (_, n) => n + 1);
        Most.AddOrUpdate(id, now, (_, seen) => Math.Max(seen, now));
        try { await Task.Delay(hold); }
        finally { Running.AddOrUpdate(id, 0, static (_, n) => n - 1); }
    }

    // Waits (real time, bounded) until two calls are inside at once; returns how many were seen together.
    public static async Task<int> Meet(string id)
    {
        Met.AddOrUpdate(id, 1, static (_, n) => n + 1);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Met.GetValueOrDefault(id) < 2 && DateTime.UtcNow < deadline) { await Task.Delay(10); }
        return Met.GetValueOrDefault(id);
    }

    public static void Tick(string id) => Ticked.AddOrUpdate(id, 1, static (_, n) => n + 1);
}

public interface ITurns
{
    Task Busy();

    [Interleaves]
    Task<int> Meet();

    Task Arm(int dueMs);
}

/// <summary>Busy calls are turns; Meet interleaves; a timer tick is a turn and counts toward the same overlap measure.</summary>
[Neuron("e2e.turns")]
public sealed class TurnNeuron : Neuron, ITurns
{
    public Task Busy() => Turns.Inside(Id.ToString(), TimeSpan.FromMilliseconds(400));

    public Task<int> Meet() => Turns.Meet(Id.ToString());

    public Task Arm(int dueMs)
    {
        Context.Timers.Start("tick", TimeSpan.FromMilliseconds(dueMs), null);
        return Task.CompletedTask;
    }

    protected override async Task OnReminder(string name, CancellationToken cancellationToken)
    {
        await Turns.Inside(Id.ToString(), TimeSpan.FromMilliseconds(100));
        Turns.Tick(Id.ToString());
    }
}
