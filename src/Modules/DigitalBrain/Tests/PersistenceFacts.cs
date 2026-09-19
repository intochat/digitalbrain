using DigitalBrain.Contracts;
using DigitalBrain.Core;
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
    public async Task HeldReadCanBeReleasedAndStateSurvivesSiloRestart()
    {
        var ct = TestContext.Current.CancellationToken;
        var faults = new StorageFaults();
        await using var brain = await DigitalBrainSimulation.StartAsync(new() { StorageFaults = faults }, ct);
        var counter = brain.Get<ICounter>("hold");
        await counter.SetWithoutPublishing(3);
        await brain.RestartSiloAsync(ct);
        using var hold = faults.HoldNextRead(counter.GetGrainId());
        var read = counter.Read();
        await hold.Entered.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.False(read.IsCompleted);
        hold.Release();
        Assert.Equal(3, await read.WaitAsync(TimeSpan.FromSeconds(5), ct));
    }

    [Fact]
    public async Task CommittedStateSurvivesANewHostWithoutReplayingSignals()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "brain-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var first = await DigitalBrainSimulation.StartAsync(new() { PersistenceDirectory = directory }, ct))
            {
                await first.Get<ICounter>("saved").SetWithoutPublishing(23);
            }
            await using var second = await DigitalBrainSimulation.StartAsync(new() { PersistenceDirectory = directory }, ct);
            var counter = second.Get<ICounter>("saved");
            Assert.Equal(23, await counter.Read());
            await using var probe = await second.Observe<Number>(counter, ct);
            await counter.Set(24);
            Assert.Equal(24, (await probe.NextAsync(ct: ct)).Value);
            await counter.Deactivate();
            Assert.Equal(24, await counter.Read());
        }
        finally { if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); } }
    }
}

