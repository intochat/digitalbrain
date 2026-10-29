using System.Text.Json.Serialization;
using DigitalBrain;
using DigitalBrain.Contracts;

namespace APP_NAMESPACE;

/// <summary>The app's private durable counter contract; its alias stays frozen after installation.</summary>
[NeuronAlias("APP_ID.counter")]
public interface ICounter
{
    Task<int> Increment();
    Task<int> Count();
}

[NeuronAlias("APP_ID.counter-state")]
public sealed record CounterState
{
    [Field(0), JsonPropertyName("count")] public int Count { get; init; }
}

[Signal("APP_ID.counted")]
public sealed record Counted([property: Field(0), JsonPropertyName("count")] int Count) : Signal;

/// <summary>State belongs to a neuron, so it survives deactivation and runtime restart.</summary>
[Neuron("APP_ID.counter")]
public sealed class CounterNeuron : Neuron<CounterState>, ICounter
{
    public async Task<int> Increment()
    {
        var next = State with { Count = State.Count + 1 };
        await Save(next, new Counted(next.Count));
        return next.Count;
    }

    public Task<int> Count() => Task.FromResult(State.Count);
}
