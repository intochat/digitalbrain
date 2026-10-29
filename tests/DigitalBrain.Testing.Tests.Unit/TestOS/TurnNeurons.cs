using System.Collections.Concurrent;
using DigitalBrain.Invocation;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

// What a test sees of the neurons it hosts: a log, gates the test opens and points the neurons report reaching.
public sealed class Probe : IServiceProvider
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _reached = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _log = new();
    private readonly ConcurrentDictionary<NeuronId, int> _activations = new();

    public IReadOnlyList<string> Log => [.. _log];

    public bool ThrowOnDeactivate { get; set; }

    public void Write(string line) => _log.Enqueue(line);

    public Task Gate(string name) => Source(_gates, name).Task;

    public void Open(string name) => Source(_gates, name).TrySetResult();

    public void Reach(string name) => Source(_reached, name).TrySetResult();

    public Task Reached(string name) => Source(_reached, name).Task.WaitAsync(TimeSpan.FromSeconds(10));

    public int Activated(NeuronId id) => _activations.AddOrUpdate(id, 1, (_, count) => count + 1);

    public int Activations(NeuronId id) => _activations.GetValueOrDefault(id);

    public object? GetService(Type serviceType) => serviceType == typeof(Probe) ? this : null;

    private static TaskCompletionSource Source(ConcurrentDictionary<string, TaskCompletionSource> sources, string name)
        => sources.GetOrAdd(name, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

public sealed record Tally
{
    public int Total { get; init; }
}

[Signal("test.counted")]
public sealed record Counted(int Total) : Signal;

public interface ITurnProbe
{
    Task<string> Hold(string tag);

    [Interleaves]
    Task<string> Quick(string tag);

    [ReadOnlyCall]
    Task<string> Look(string tag);

    [OneWayCall]
    Task Note(string tag);

    Task<string> Echo(string text);

    Task<int> Activations();

    Task Count(int by);

    Task<int> Total();

    Task StartTimers();

    Task<string> Call(NeuronId other);

    Task<string> Bounce(NeuronId via);

    Task<string> BounceBack(NeuronId back);

    Task DeferEcho(string tag);
}

public abstract class ProbeNeuron(Probe probe) : Neuron<Tally>, ITurnProbe
{
    protected Probe Probe { get; } = probe;

    public async Task<string> Hold(string tag)
    {
        Probe.Write($"start:{tag}");
        Probe.Reach(tag);
        await Probe.Gate(tag);
        Probe.Write($"end:{tag}");
        return tag;
    }

    public Task<string> Quick(string tag)
    {
        Probe.Write($"quick:{tag}");
        return Task.FromResult(tag);
    }

    public Task<string> Look(string tag)
    {
        Probe.Write($"look:{tag}");
        return Task.FromResult(tag);
    }

    public Task Note(string tag)
    {
        Probe.Write($"note:{tag}");
        return Task.CompletedTask;
    }

    public Task<string> Echo(string text) => Task.FromResult(text);

    public Task<int> Activations() => Task.FromResult(Probe.Activations(Id));

    public Task Count(int by)
    {
        var next = State with { Total = State.Total + by };
        return Save(next, new Counted(next.Total));
    }

    public Task<int> Total() => Task.FromResult(State.Total);

    public async Task StartTimers()
    {
        Context.Timers.Start("tick", TimeSpan.FromSeconds(1), null);
        Context.Timers.Start("every", TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        await Context.Reminders.Start("remind", TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public Task<string> Call(NeuronId other) => Get<ITurnProbe>(other).Echo("called");

    public Task<string> Bounce(NeuronId via) => Get<ITurnProbe>(via).BounceBack(Id);

    public Task<string> BounceBack(NeuronId back) => Get<ITurnProbe>(back).Echo("cycle");

    public Task DeferEcho(string tag)
    {
        // A call to itself would wait behind this turn; deferred, it runs once the turn has ended.
        Defer(async () => Probe.Write($"deferred:{await Get<ITurnProbe>(Id).Echo(tag)}"));
        Probe.Write($"turn:{tag}");
        return Task.CompletedTask;
    }

    protected override Task OnReminder(string name, CancellationToken cancellationToken)
    {
        Probe.Write($"fired:{name}");
        return Task.CompletedTask;
    }

    protected override Task OnActivate(CancellationToken cancellationToken)
    {
        Probe.Activated(Id);
        return Task.CompletedTask;
    }

    protected override Task OnDeactivate(DeactivationCause cause, CancellationToken cancellationToken)
    {
        Probe.Write($"deactivated:{cause}");
        if (Probe.ThrowOnDeactivate) { throw new InvalidOperationException("deactivation failed"); }
        return Task.CompletedTask;
    }
}

[Neuron("turn.probe")]
public sealed class TurnProbe(Probe probe) : ProbeNeuron(probe);

// Calls whose tag starts with "q" interleave; every other call takes the turn.
[InterleavesWhen(nameof(QuickTag))]
public abstract class WhenProbeBase(Probe probe) : ProbeNeuron(probe)
{
    public static bool QuickTag(RuntimeInvocation call)
        => call.Arguments is [{ ValueKind: System.Text.Json.JsonValueKind.String } tag] && tag.GetString()!.StartsWith('q');
}

// [InterleavesWhen] is inherited from the base.
[Neuron("turn.when")]
public sealed class WhenProbe(Probe probe) : WhenProbeBase(probe);

[Neuron("turn.reentrant")]
[Reentrant]
public sealed class ReentrantProbe(Probe probe) : ProbeNeuron(probe);

[Neuron("turn.stateless")]
[StatelessNeuron]
public sealed class StatelessProbe(Probe probe) : ProbeNeuron(probe);
