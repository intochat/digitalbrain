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
    Task Deactivate();
}
[GenerateSerializer]
public sealed class CounterState { [Id(0)] public int Value { get; set; } }
[GrainType("test-counter")]
public sealed class CounterNeuron([PersistentState("state", "Default")] IPersistentState<CounterState> state) : Neuron, ICounter
{
    private bool _unavailable;
    public Task<int> Read() { Guard(); return Task.FromResult(state.State.Value); }
    public Task Deactivate() { DeactivateOnIdle(); return Task.CompletedTask; }
    public async Task Set(int value)
    {
        await SetWithoutPublishing(value);
        await PublishAsync(new Number(value));
    }
    public async Task SetWithoutPublishing(int value)
    {
        Guard();
        state.State = new CounterState { Value = value };
        try { await state.WriteStateAsync(); }
        catch
        {
            try { await state.ReadStateAsync(); }
            catch { _unavailable = true; DeactivateOnIdle(); }
            throw;
        }
    }
    private void Guard() { if (_unavailable) { throw new InvalidOperationException("State unavailable."); } }
}

public sealed class PersistenceFacts
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefusedWritesDoNotExposeTentativeStateOrEmitSuccess(bool refuseReload)
    {
        var ct = TestContext.Current.CancellationToken;
        var faults = new StorageFaults();
        await using var brain = await DigitalBrainSimulation.StartAsync(new() { StorageFaults = faults }, ct);
        var counter = brain.Get<ICounter>("refuse");
        await counter.SetWithoutPublishing(4);
        await using var probe = await brain.Observe<Number>(counter, ct);
        faults.FailNextWrite(counter.GetGrainId());
        if (refuseReload) { faults.FailNextRead(counter.GetGrainId()); }
        var error = await Assert.ThrowsAsync<OrleansException>(() => counter.Set(5));
        Assert.IsType<IOException>(error.InnerException);
        Assert.Equal(4, await counter.Read());
        if (!refuseReload)
        {
            await counter.Set(6);
            Assert.Equal(6, (await probe.NextAsync(ct: ct)).Value);
            Assert.DoesNotContain(probe.Snapshot, fact => fact.Value == 5);
        }
    }

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

