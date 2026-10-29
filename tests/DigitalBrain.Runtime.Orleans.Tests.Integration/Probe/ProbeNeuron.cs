using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Orleans.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Probe;

/// <summary>Attribute-free state of the probe (06 D-14: JSON payloads).</summary>
public sealed record ProbeState
{
    public int Value { get; init; }
}

[Signal("probe.shapes.saved")]
public sealed record ProbeSaved(int Value, string Note) : Signal;

/// <summary>What the probes saw, keyed by tag or gate; facts use unique keys.</summary>
internal static class ShapeLog
{
    public static readonly ConcurrentDictionary<string, int> Seen = new();
    public static readonly ConcurrentDictionary<string, TaskCompletionSource> Gates = new();

    public static TaskCompletionSource Gate(string id) => Gates.GetOrAdd(id, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    public static void Mark(string tag) => Seen.AddOrUpdate(tag, 1, static (_, count) => count + 1);

    public static async Task Until(Func<bool> condition, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition()) { await Task.Delay(20, limit.Token); }
    }
}

/// <summary>The probe body, shared by the exclusive, reentrant and stateless variants.</summary>
[InterleavesWhen(nameof(Rule))]
public abstract class ProbeNeuronBase : Neuron<ProbeState>, IProbe
{
    private static bool Rule(RuntimeInvocation call) => call.Method == nameof(Flagged) && call.Arguments[0].GetBoolean();

    public Task Touch(string tag)
    {
        ShapeLog.Mark(tag);
        return Task.CompletedTask;
    }

    public Task<int> Add(int a, int b) => Task.FromResult(a + b);

    public ValueTask Nothing(string tag)
    {
        ShapeLog.Mark(tag);
        return ValueTask.CompletedTask;
    }

    public ValueTask<string> Echo(string text) => ValueTask.FromResult(text);

    public async Task<bool> WaitCancelled(string tag, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return false;
        }
        catch (OperationCanceledException)
        {
            ShapeLog.Mark(tag);
            throw;
        }
    }

    public async IAsyncEnumerable<int> Count(int upTo, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < upTo; i++)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
        }
    }

    public async Task Fire(string gate)
    {
        await ShapeLog.Gate(gate).Task;
        ShapeLog.Mark(gate);
    }

    public Task Slow() => Task.Delay(TimeSpan.FromSeconds(5));

    public async Task Hold(string gate)
    {
        ShapeLog.Mark("held:" + gate);
        await ShapeLog.Gate(gate).Task;
    }

    public Task<int> Plain() => Task.FromResult(1);

    public Task<int> Peek() => Task.FromResult(2);

    public Task<int> Look() => Task.FromResult(3);

    public Task<int> Flagged(bool interleave) => Task.FromResult(interleave ? 4 : 5);

    public async Task<int> Save(int value, string note)
    {
        await Save(State with { Value = value }, new ProbeSaved(value, note));
        return State.Value;
    }

    public Task<int> Value() => Task.FromResult(State.Value);

    public Task<string> Caller() => Task.FromResult(((IHostNeuronContext)Context).Caller);

    public Task<string> AskCaller(string otherKey) => Get<IProbe>(new NeuronId(Id.Brain, "probe.shapes", otherKey)).Caller();

    public Task<int> Instance() => Task.FromResult(RuntimeHelpers.GetHashCode(this));
}

[Neuron("probe.shapes", State = "probe")]
public sealed class ProbeNeuron : ProbeNeuronBase;

[Neuron("probe.shapes.reentrant", State = "probe"), Reentrant]
public sealed class ReentrantProbeNeuron : ProbeNeuronBase;

[Neuron("probe.shapes.stateless"), StatelessNeuron]
public sealed class StatelessProbeNeuron : ProbeNeuronBase;
