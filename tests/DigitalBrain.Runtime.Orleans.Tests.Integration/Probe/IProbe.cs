using DigitalBrain.Contracts;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Probe;

// 04-14: the probe contract covering every call shape a plain neuron may declare. No Orleans type, base or attribute.

/// <summary>Every call shape: return kinds, cancellation, streams, neutral call attributes and state.</summary>
[NeuronAlias("test.probe.shapes")]
public interface IProbe
{
    Task Touch(string tag);

    Task<int> Add(int a, int b);

    ValueTask Nothing(string tag);

    ValueTask<string> Echo(string text);

    Task<bool> WaitCancelled(string tag, CancellationToken cancellationToken);

    IAsyncEnumerable<int> Count(int upTo, CancellationToken cancellationToken);

    [OneWayCall]
    Task Fire(string gate);

    [CallTimeout(1000)]
    Task Slow();

    Task Hold(string gate);

    Task<int> Plain();

    [Interleaves]
    Task<int> Peek();

    [ReadOnlyCall]
    Task<int> Look();

    Task<int> Flagged(bool interleave);

    Task<int> Save(int value, string note);

    Task<int> Value();

    Task<string> Caller();

    Task<string> AskCaller(string otherKey);

    Task<int> Instance();
}
