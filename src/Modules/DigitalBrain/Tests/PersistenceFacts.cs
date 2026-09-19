using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Orleans;
using Orleans.Runtime;
using Xunit;
namespace DigitalBrain.Tests;

public interface ICounter : INeuron
{
    Task<int> Read();
    Task Set(int value);
    Task SetWithoutPublishing(int value);
}
[GenerateSerializer]
public sealed class CounterState { [Id(0)] public int Value { get; set; } }
[GrainType("test-counter")]
public sealed class CounterNeuron([PersistentState("state", "Default")] IPersistentState<CounterState> state) : Neuron, ICounter
{
    public Task<int> Read() => Task.FromResult(state.State.Value);
    public async Task Set(int value)
    {
        await SetWithoutPublishing(value);
        await PublishAsync(new Number(value));
    }
    public async Task SetWithoutPublishing(int value)
    {
        state.State = new CounterState { Value = value };
        await state.WriteStateAsync();
    }
}

public sealed class PersistenceFacts
{
    [Fact]
    public async Task DeactivationKeepsStateAndDoesNotReplaySignals()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await DigitalBrainSimulation.StartAsync(cancellationToken: ct);
        var counter = brain.Get<ICounter>("saved");
        await counter.SetWithoutPublishing(23);
        await brain.DeactivateAsync(counter, ct);
        Assert.Equal(23, await counter.Read());
        await using var probe = await brain.Observe<Number>(counter, ct);
        await counter.Set(24);
        Assert.Equal(24, (await probe.NextAsync(ct: ct)).Value);
        Assert.DoesNotContain(probe.Snapshot, fact => fact.Value == 23);
    }
}
