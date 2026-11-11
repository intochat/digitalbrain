using DigitalBrain.Confirmation;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Operations;
using OS = DigitalBrain.Testing.TestOS;
using Xunit;

#pragma warning disable CA1859

namespace DigitalBrain.Testing.Tests.Unit.Neurons;

public sealed record SeatProbeState(int Counter, string Message)
{
    public SeatProbeState() : this(0, string.Empty) { }
}

public sealed record SeatProbeIncremented(int NewValue) : Signal;

public interface ISeatProbe
{
    Task<int> GetCounter();
    Task<string> GetMessage();
    Task Increment(int by, string msg);
    Task ClearSeat();
    Task DirectSave(int val, string msg);
    Task<SeatProbeState?> DirectLoad();
    Task<bool> HasInjectedServices();
    Task<NeuronId> GetInjectedNeuronId();
}

[Neuron("seat-probe")]
public sealed class SeatProbeNeuron : Neuron<SeatProbeState>, ISeatProbe
{
    private readonly IOperations? _operations;
    private readonly IOperationsQuery? _query;
    private readonly ISignalHistory? _history;
    private readonly IConfirmations? _confirmations;

    public SeatProbeNeuron(
        IOperations operations,
        IOperationsQuery query,
        ISignalHistory history,
        IConfirmations confirmations)
    {
        _operations = operations;
        _query = query;
        _history = history;
        _confirmations = confirmations;
    }

    public Task<int> GetCounter() => Task.FromResult(State.Counter);
    public Task<string> GetMessage() => Task.FromResult(State.Message);

    public Task Increment(int by, string msg)
    {
        var next = State with { Counter = State.Counter + by, Message = msg };
        return Save(next, new SeatProbeIncremented(next.Counter));
    }

    public async Task ClearSeat()
    {
        await Seat.Clear();
    }

    public Task DirectSave(int val, string msg)
    {
        var next = new SeatProbeState(val, msg);
        return Seat.Save(next, [new SeatProbeIncremented(val)]);
    }

    public Task<SeatProbeState?> DirectLoad() => Seat.Load<SeatProbeState>();

    public Task<bool> HasInjectedServices() =>
        Task.FromResult(_operations is not null &&
        _query is not null &&
        _history is not null &&
        _confirmations is not null);

    public Task<NeuronId> GetInjectedNeuronId() => Task.FromResult(Id);
}

public sealed class TestNeuronSeatLifecycleFacts
{
    private static readonly BrainId BrainAlpha = new("test-brain-alpha");
    private static readonly BrainId BrainBeta = new("test-brain-beta");
    private static readonly NeuronId ProbeAlpha = new(BrainAlpha, "seat-probe", "alpha-1");
    private static readonly NeuronId ProbeBeta = new(BrainBeta, "seat-probe", "beta-1");

    private static OS CreateTestOS()
    {
        var os = OS.Local().Register(typeof(TestNeuronSeatLifecycleFacts).Assembly);
        os.Confirmations.ApproveAll();
        return os;
    }

    [Fact]
    public async Task NeuronServices_ConstructorInjection_AllFourCoreServicesInjected()
    {
        await using var os = CreateTestOS();
        var probe = os.Get<ISeatProbe>(ProbeAlpha);

        await os.Activate(ProbeAlpha);
        await os.Idle();

        var hasServices = await probe.HasInjectedServices();
        Assert.True(hasServices, "All 4 DI services (IOperations, IOperationsQuery, ISignalHistory, IConfirmations) must be resolved.");

        var id = await probe.GetInjectedNeuronId();
        Assert.Equal(ProbeAlpha, id);
    }

    [Fact]
    public async Task INeuronSeat_TestNeuronContext_LoadSaveClearLifecycleAcrossRestarts()
    {
        await using var os = CreateTestOS();
        var probe = os.Get<ISeatProbe>(ProbeAlpha);

        await os.Activate(ProbeAlpha);
        await os.Idle();

        // 1. Initial State should be default (0, "")
        Assert.Equal(0, await probe.GetCounter());
        Assert.Equal(string.Empty, await probe.GetMessage());

        // 2. Perform state updates via Save(next, signal)
        await probe.Increment(5, "first-step");
        await os.Idle();

        Assert.Equal(5, await probe.GetCounter());
        Assert.Equal("first-step", await probe.GetMessage());

        // 3. Restart TestOS to simulate activation eviction and reload from journal
        await os.Restart();
        await os.Activate(ProbeAlpha);
        await os.Idle();

        Assert.Equal(5, await probe.GetCounter());
        Assert.Equal("first-step", await probe.GetMessage());

        // 4. Clear state via INeuronSeat.Clear()
        await probe.ClearSeat();
        await os.Idle();

        // Direct Load after clear returns null
        var loadedAfterClear = await probe.DirectLoad();
        Assert.Null(loadedAfterClear);

        // 5. Restart again to verify clear persists across host restarts
        await os.Restart();
        await os.Activate(ProbeAlpha);
        await os.Idle();

        // Once re-activated with cleared state, State should re-initialize to default new()
        Assert.Equal(0, await probe.GetCounter());
        Assert.Equal(string.Empty, await probe.GetMessage());

        // 6. Direct Save via INeuronSeat works after Clear
        await probe.DirectSave(42, "resurrected");
        await os.Idle();

        var reloaded = await probe.DirectLoad();
        Assert.NotNull(reloaded);
        Assert.Equal(42, reloaded.Counter);
        Assert.Equal("resurrected", reloaded.Message);
    }

    [Fact]
    public async Task NeuronServices_CrossActivationAndCrossBrainIsolation_NoStateLeak()
    {
        await using var os = CreateTestOS();
        var probeAlpha = os.Get<ISeatProbe>(ProbeAlpha);
        var probeBeta = os.Get<ISeatProbe>(ProbeBeta);

        await os.Activate(ProbeAlpha);
        await os.Activate(ProbeBeta);
        await os.Idle();

        // Mutate Alpha
        await probeAlpha.Increment(10, "alpha-data");
        await os.Idle();

        // Verify Alpha has state, Beta remains pristine default
        Assert.Equal(10, await probeAlpha.GetCounter());
        Assert.Equal("alpha-data", await probeAlpha.GetMessage());

        Assert.Equal(0, await probeBeta.GetCounter());
        Assert.Equal(string.Empty, await probeBeta.GetMessage());

        // Mutate Beta
        await probeBeta.Increment(25, "beta-data");
        await os.Idle();

        // Both states are preserved independently
        Assert.Equal(10, await probeAlpha.GetCounter());
        Assert.Equal(25, await probeBeta.GetCounter());

        // Clearing Alpha does not affect Beta
        await probeAlpha.ClearSeat();
        await os.Idle();

        Assert.Null(await probeAlpha.DirectLoad());
        var betaState = await probeBeta.DirectLoad();
        Assert.NotNull(betaState);
        Assert.Equal(25, betaState.Counter);
        Assert.Equal("beta-data", betaState.Message);
    }
}
