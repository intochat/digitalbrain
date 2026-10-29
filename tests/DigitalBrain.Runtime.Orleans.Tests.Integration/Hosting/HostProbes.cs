using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Spi;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

// Plain neuron classes: no Orleans namespace, no grain base. The host grains run them.

/// <summary>What the probes saw, keyed by neuron id; facts use unique keys so they never share an entry.</summary>
internal static class ProbeLog
{
    public static readonly ConcurrentDictionary<string, int> Touched = new();
    public static readonly ConcurrentDictionary<string, bool> Cancelled = new();
    public static readonly ConcurrentDictionary<string, bool> StreamStopped = new();
    public static readonly ConcurrentDictionary<string, bool> Fired = new();
    public static readonly ConcurrentDictionary<string, TaskCompletionSource> Gates = new();

    public static TaskCompletionSource Gate(string id) => Gates.GetOrAdd(id, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public static async Task Until(Func<bool> condition, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition()) { await Task.Delay(20, limit.Token); }
    }
}

[NeuronAlias("test.probe")]
public interface IProbe
{
    Task Touch();

    Task<int> Add(int a, int b);

    ValueTask<string> Echo(string text);

    ValueTask Nothing();

    Task<bool> WaitCancelled(CancellationToken cancellationToken);

    IAsyncEnumerable<int> Count(CancellationToken cancellationToken);

    [OneWayCall]
    Task Fire();

    [CallTimeout(1000)]
    Task Slow();

    Task<string> Key();

    Task<string> Caller();

    Task<string> Silo();

    Task<string> AskCaller(string otherKey);
}

[Neuron("probe")]
public sealed class ProbeNeuron : Neuron, IProbe
{
    private string Me => Id.ToString();

    public Task Touch()
    {
        ProbeLog.Touched.AddOrUpdate(Me, 1, static (_, count) => count + 1);
        return Task.CompletedTask;
    }

    public Task<int> Add(int a, int b) => Task.FromResult(a + b);

    public ValueTask<string> Echo(string text) => ValueTask.FromResult(text + "@" + Me);

    public ValueTask Nothing() => ValueTask.CompletedTask;

    public async Task<bool> WaitCancelled(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return false;
        }
        catch (OperationCanceledException)
        {
            ProbeLog.Cancelled[Me] = true;
            throw;
        }
    }

    public async IAsyncEnumerable<int> Count([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            for (var i = 0; ; i++)
            {
                yield return i;
                await Task.Delay(20, cancellationToken);
            }
        }
        finally { ProbeLog.StreamStopped[Me] = true; }
    }

    public async Task Fire()
    {
        await ProbeLog.Gate(Me).Task;
        ProbeLog.Fired[Me] = true;
    }

    public Task Slow() => Task.Delay(TimeSpan.FromSeconds(5));

    public Task<string> Key() => Task.FromResult(Me);

    public Task<string> Caller() => Task.FromResult(((IHostNeuronContext)Context).Caller);

    public Task<string> Silo() => Task.FromResult(((IHostNeuronContext)Context).Silo);

    public Task<string> AskCaller(string otherKey) => Get<IProbe>(new NeuronId(Id.Brain, "probe", otherKey)).Caller();
}

public interface IStatelessProbe
{
    Task<int> Busy();
}

[Neuron("probe.stateless"), StatelessNeuron]
public sealed class StatelessProbe : Neuron, IStatelessProbe
{
    private static int _running;

    private static int _most;

    public static int MostConcurrent => Volatile.Read(ref _most);

    public async Task<int> Busy()
    {
        var now = Interlocked.Increment(ref _running);
        int seen;
        while ((seen = Volatile.Read(ref _most)) < now && Interlocked.CompareExchange(ref _most, now, seen) != seen) { }
        await Task.Delay(300);
        Interlocked.Decrement(ref _running);
        return RuntimeHelpers.GetHashCode(this);
    }
}

/// <summary>Builds an invocation by hand, as a forged or misdirected caller would.</summary>
internal static class Calls
{
    public static RuntimeInvocation Of(string behavior, string key, string method)
        => new(behavior, NeuronProxy.ContractName(typeof(IProbe)), key, method, []);
}

/// <summary>Turns observed by the interleaving facts, keyed by gate or tag.</summary>
internal static class TurnLog
{
    public static readonly ConcurrentDictionary<string, bool> Held = new();
    public static readonly ConcurrentDictionary<string, bool> Noted = new();
}

public interface ITurnProbe
{
    Task Hold(string gate);

    [Interleaves]
    Task HoldNow(string gate);

    [Interleaves]
    Task<int> Peek();

    [ReadOnlyCall]
    Task<int> Read();

    Task<int> Plain();

    Task<int> Flagged(bool interleave);

    [OneWayCall]
    Task Note(string tag);

    [OneWayCall, Interleaves]
    Task NoteNow(string tag);

    IAsyncEnumerable<int> Items();

    [Interleaves]
    IAsyncEnumerable<int> ItemsNow();
}

/// <summary>An abstract base with an inherited [InterleavesWhen]: Flagged(true) interleaves.</summary>
[InterleavesWhen(nameof(BaseRule))]
public abstract class TurnProbeBase : Neuron, ITurnProbe
{
    private static bool BaseRule(RuntimeInvocation call) => call.Method == nameof(Flagged) && call.Arguments[0].GetBoolean();

    public async Task Hold(string gate)
    {
        TurnLog.Held[gate] = true;
        await ProbeLog.Gate(gate).Task;
    }

    public Task HoldNow(string gate) => Hold(gate);

    public Task<int> Peek() => Task.FromResult(1);

    public Task<int> Read() => Task.FromResult(2);

    public Task<int> Plain() => Task.FromResult(3);

    public Task<int> Flagged(bool interleave) => Task.FromResult(interleave ? 4 : 5);

    public Task Note(string tag)
    {
        TurnLog.Noted[tag] = true;
        return Task.CompletedTask;
    }

    public Task NoteNow(string tag) => Note(tag);

    public async IAsyncEnumerable<int> Items()
    {
        await Task.Yield();
        yield return 6;
    }

    public IAsyncEnumerable<int> ItemsNow() => Items();
}

[Neuron("turn")]
public sealed class TurnProbe : TurnProbeBase;

/// <summary>Redeclares [InterleavesWhen]: its own rule (Plain interleaves) replaces the base rule.</summary>
[Neuron("turn.own"), InterleavesWhen(nameof(OwnRule))]
public sealed class OwnRuleTurnProbe : TurnProbeBase
{
    private static bool OwnRule(RuntimeInvocation call) => call.Method == nameof(Plain);
}

[Neuron("turn.reentrant"), Reentrant]
public sealed class ReentrantTurnProbe : TurnProbeBase;

/// <summary>Attribute-free state of the counter probe (06 D-14: JSON payloads).</summary>
public sealed record CounterState
{
    public int Value { get; init; }
}

[Signal("probe.counted")]
public sealed record Counted(int Value) : Signal;

[Signal("probe.noted.deep")]
public sealed record Noted(string Text) : Signal;

public interface ICounter
{
    Task<int> Add(int by);

    Task<int> Value();

    Task Note(string text);

    Task Tick(string name, int dueMs);

    Task Remind(string name, int dueMs, int periodMs);

    Task<string> Caller();

    Task<string> AskCaller(string otherKey);

    Task<string> ReadForeign(string publisher);

    Task<long> HistoryLast();
}

/// <summary>What the counter probes saw: timer and reminder ticks, by neuron id and name.</summary>
internal static class CounterLog
{
    public static readonly ConcurrentDictionary<string, int> Ticks = new();
}

[Neuron("probe.counter", State = "count")]
public sealed class CounterNeuron : Neuron<CounterState>, ICounter
{
    public async Task<int> Add(int by)
    {
        var next = State with { Value = State.Value + by };
        await Save(next, new Counted(next.Value));
        return State.Value;
    }

    public Task<int> Value() => Task.FromResult(State.Value);

    public Task Note(string text) => Publish(new Noted(text));

    public Task Tick(string name, int dueMs)
    {
        Context.Timers.Start(name, TimeSpan.FromMilliseconds(dueMs), null);
        return Task.CompletedTask;
    }

    public Task Remind(string name, int dueMs, int periodMs)
        => Context.Reminders.Start(name, TimeSpan.FromMilliseconds(dueMs), TimeSpan.FromMilliseconds(periodMs));

    public Task<string> Caller() => Task.FromResult(((IHostNeuronContext)Context).Caller);

    public Task<string> AskCaller(string otherKey) => Get<ICounter>(new NeuronId(Id.Brain, "probe.counter", otherKey)).Caller();

    public async Task<string> ReadForeign(string publisher)
    {
        try
        {
            _ = await Context.History.Read(NeuronId.Parse(publisher), 0, 10, CancellationToken.None);
            return "read";
        }
        catch (UnauthorizedAccessException) { return "refused"; }
    }

    public async Task<long> HistoryLast() => (await Context.History.Read(Id, 0, 10, CancellationToken.None)).Last;

    protected override async Task OnReminder(string name, CancellationToken cancellationToken)
    {
        CounterLog.Ticks.AddOrUpdate($"{Id}:{name}", 1, static (_, count) => count + 1);
        if (name.StartsWith("once", StringComparison.Ordinal)) { await Context.Reminders.Stop(name); }
    }
}

public interface IStatelessSaver
{
    Task Save();
}

[Neuron("probe.stateless.saver"), StatelessNeuron]
public sealed class StatelessSaver : Neuron<CounterState>, IStatelessSaver
{
    public Task Save() => Save(new CounterState { Value = 1 }, new Counted(1));
}
