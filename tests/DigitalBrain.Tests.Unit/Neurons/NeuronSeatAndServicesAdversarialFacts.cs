using System.Reflection;
using DigitalBrain.Confirmation;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Operations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA1859 // Use concrete types when possible for performance

namespace DigitalBrain.Tests.Unit.Neurons;

public sealed record SampleNeuronState(int Value, string Label)
{
    public SampleNeuronState() : this(0, string.Empty) { }
}

public sealed record SampleValueChanged(int Value) : Signal;

public sealed record SampleLabelUpdated(string Label) : Signal;

[Neuron("adversarial-probe")]
public sealed class AdversarialProbeNeuron : Neuron<SampleNeuronState>
{
    public SampleNeuronState CurrentState => State;
    public bool CommittedCalled { get; private set; }

    public Task Update(int val, string lbl) =>
        Save(new SampleNeuronState(val, lbl), [new SampleValueChanged(val), new SampleLabelUpdated(lbl)]);

    public Task UpdateSingle(int val) =>
        Save(new SampleNeuronState(val, State.Label), new SampleValueChanged(val));

    public Task AttemptEmptySave() =>
        Save(new SampleNeuronState(99, "empty"), []);

    public Task AttemptNullSignalListSave() =>
        Save(new SampleNeuronState(99, "null-list"), (IReadOnlyList<Signal>)null!);

    public Task AttemptNullSignalInListSave() =>
        Save(new SampleNeuronState(99, "null-item"), [new SampleValueChanged(1), null!]);

    public Task AttemptNullStateSave() =>
        Save(null!, new SampleValueChanged(1));

    public Task AttemptNullSignalSave() =>
        Save(new SampleNeuronState(99, "null-sig"), (Signal)null!);

    protected override void OnCommitted()
    {
        CommittedCalled = true;
    }
}

public sealed class FaultyFailingSeat : INeuronSeat, INeuronContext
{
    public NeuronId Id => new(new BrainId("b"), "type", "key");
    public DigitalBrain.Access.Caller? Caller => null;
    public TimeProvider Time => TimeProvider.System;
    public ITimers Timers => throw new NotImplementedException();
    public IReminders Reminders => throw new NotImplementedException();
    public ISynapses Synapses => throw new NotImplementedException();
    public IServiceProvider Services => new ServiceCollection().BuildServiceProvider();

    public Task<TState?> Load<TState>() where TState : class => Task.FromResult<TState?>(null);

    public Task Save<TState>(TState next, IReadOnlyList<Signal> signals) where TState : class =>
        throw new InvalidOperationException("Simulated persistent store failure");

    public Task Clear() => Task.CompletedTask;
    public Task Publish(Signal signal) => Task.CompletedTask;
    public T Get<T>(NeuronId id) where T : class => throw new NotImplementedException();
    public void Defer(Func<Task> work) => work();
}

public sealed class NeuronSeatAndServicesAdversarialFacts
{
    private static readonly BrainId BrainAlpha = new("brain-alpha");
    private static readonly BrainId BrainBeta = new("brain-beta");
    private static readonly NeuronId NeuronA1 = new(BrainAlpha, "worker", "1");
    private static readonly NeuronId NeuronA2 = new(BrainAlpha, "worker", "2");
    private static readonly NeuronId NeuronB1 = new(BrainBeta, "worker", "1");

    #region 1. NeuronServices Constructor DI & Per-Activation Caching

    [Fact]
    public void NeuronServices_ResolvesRegisteredBindings()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var operationsMock = new object();
        var historyMock = new object();

        var bindings = new NeuronBinding[]
        {
            new(typeof(IOperations), (sp, id) => operationsMock),
            new(typeof(ISignalHistory), (sp, id) => historyMock),
        };

        var neuronServices = new NeuronServices(services, NeuronA1, bindings);

        Assert.Same(operationsMock, neuronServices.GetService(typeof(IOperations)));
        Assert.Same(historyMock, neuronServices.GetService(typeof(ISignalHistory)));
    }

    [Fact]
    public void NeuronServices_CachesPerActivationBindingInstances_InvokesFactoryExactlyOnce()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var invocationCount = 0;

        var bindings = new NeuronBinding[]
        {
            new(typeof(IOperations), (sp, id) =>
            {
                Interlocked.Increment(ref invocationCount);
                return new object();
            }),
        };

        var neuronServices = new NeuronServices(services, NeuronA1, bindings);

        var first = neuronServices.GetService(typeof(IOperations));
        var second = neuronServices.GetService(typeof(IOperations));
        var third = neuronServices.GetService(typeof(IOperations));

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Same(second, third);
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public void NeuronServices_ResolvesFromHostWhenBindingNotPresent()
    {
        var hostServices = new ServiceCollection()
            .AddSingleton<TimeProvider>(TimeProvider.System)
            .BuildServiceProvider();

        var neuronServices = new NeuronServices(hostServices, NeuronA1, []);

        var resolved = neuronServices.GetService(typeof(TimeProvider));
        Assert.NotNull(resolved);
        Assert.Same(TimeProvider.System, resolved);
        Assert.Null(neuronServices.GetService(typeof(IOperations)));
    }

    [Fact]
    public void NeuronServices_NullServiceType_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var neuronServices = new NeuronServices(services, NeuronA1, []);

        Assert.Throws<ArgumentNullException>(() => neuronServices.GetService(null!));
    }

    [Fact]
    public void NeuronServices_CrossActivationIsolation_NoLeakageBetweenActivations()
    {
        var hostServices = new ServiceCollection().BuildServiceProvider();

        // Factory creates unique instance referencing target neuron id
        var bindings = new NeuronBinding[]
        {
            new(typeof(IOperations), (sp, id) => (Id: id, Instance: new object())),
            new(typeof(ISignalHistory), (sp, id) => (Id: id, Instance: new object())),
            new(typeof(IConfirmations), (sp, id) => (Id: id, Instance: new object())),
        };

        var activationA1 = new NeuronServices(hostServices, NeuronA1, bindings);
        var activationA2 = new NeuronServices(hostServices, NeuronA2, bindings);
        var activationB1 = new NeuronServices(hostServices, NeuronB1, bindings);

        var opA1 = activationA1.GetService(typeof(IOperations));
        var opA2 = activationA2.GetService(typeof(IOperations));
        var opB1 = activationB1.GetService(typeof(IOperations));

        Assert.NotNull(opA1);
        Assert.NotNull(opA2);
        Assert.NotNull(opB1);

        // Assert strictly distinct instances across activations
        Assert.NotSame(opA1, opA2);
        Assert.NotSame(opA1, opB1);
        Assert.NotSame(opA2, opB1);

        // Verify neuron id was bound accurately to each activation
        var typedA1 = ((NeuronId Id, object Instance))opA1;
        var typedA2 = ((NeuronId Id, object Instance))opA2;
        var typedB1 = ((NeuronId Id, object Instance))opB1;

        Assert.Equal(NeuronA1, typedA1.Id);
        Assert.Equal(NeuronA2, typedA2.Id);
        Assert.Equal(NeuronB1, typedB1.Id);
    }

    [Fact]
    public async Task NeuronServices_ConcurrentResolution_ThreadSafeAndExecutesFactoryOnce()
    {
        var hostServices = new ServiceCollection().BuildServiceProvider();
        var factoryExecutionCount = 0;

        var bindings = new NeuronBinding[]
        {
            new(typeof(IOperations), (sp, id) =>
            {
                Thread.Sleep(10); // widen race window
                Interlocked.Increment(ref factoryExecutionCount);
                return new object();
            }),
        };

        var neuronServices = new NeuronServices(hostServices, NeuronA1, bindings);

        const int concurrency = 50;
        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => Task.Run(() => neuronServices.GetService(typeof(IOperations))))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, factoryExecutionCount);
        Assert.Equal(concurrency, results.Length);
        var first = results[0];
        Assert.NotNull(first);
        foreach (var result in results)
        {
            Assert.Same(first, result);
        }
    }

    #endregion

    #region 2. Neuron<TState> Mutation Invariants & Journaled Signals

    [Fact]
    public void NeuronOfTState_StateProperty_HasNoPublicOrProtectedSetter()
    {
        var stateProp = typeof(Neuron<SampleNeuronState>).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(stateProp);
        Assert.NotNull(stateProp.SetMethod);
        Assert.False(stateProp.SetMethod.IsPublic);
        Assert.False(stateProp.SetMethod.IsFamily); // Not protected
        Assert.True(stateProp.SetMethod.IsPrivate);  // Strictly private to base
    }

    [Fact]
    public async Task NeuronOfTState_SaveWithSignals_PersistsAndUpdatesStateAtomically()
    {
        var neuron = new AdversarialProbeNeuron();
        var context = new RecordingContext(NeuronA1);
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, CancellationToken.None);

        Assert.Equal(0, neuron.CurrentState.Value);
        Assert.Equal(string.Empty, neuron.CurrentState.Label);

        await neuron.Update(42, "hello");

        Assert.Equal(42, neuron.CurrentState.Value);
        Assert.Equal("hello", neuron.CurrentState.Label);
        Assert.True(neuron.CommittedCalled);

        var write = Assert.Single(context.Writes);
        Assert.Equal(new SampleNeuronState(42, "hello"), write.State);
        Assert.Equal(2, write.Signals.Count);
        Assert.Equal(new SampleValueChanged(42), write.Signals[0]);
        Assert.Equal(new SampleLabelUpdated("hello"), write.Signals[1]);
    }

    [Fact]
    public async Task NeuronOfTState_SaveSingleSignal_Succeeds()
    {
        var neuron = new AdversarialProbeNeuron();
        var context = new RecordingContext(NeuronA1);
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, CancellationToken.None);

        await neuron.UpdateSingle(99);

        Assert.Equal(99, neuron.CurrentState.Value);
        var write = Assert.Single(context.Writes);
        Assert.Equal([new SampleValueChanged(99)], write.Signals);
    }

    [Fact]
    public async Task NeuronOfTState_SaveWithEmptySignals_ThrowsArgumentException()
    {
        var neuron = new AdversarialProbeNeuron();
        var context = new RecordingContext(NeuronA1);
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => neuron.AttemptEmptySave());
        Assert.Contains("at least one signal", ex.Message);
        Assert.Equal(0, neuron.CurrentState.Value); // Unmodified
        Assert.Empty(context.Writes);
    }

    [Fact]
    public async Task NeuronOfTState_SaveWithNullSignalList_ThrowsArgumentNullException()
    {
        var neuron = new AdversarialProbeNeuron();
        var context = new RecordingContext(NeuronA1);
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentNullException>(() => neuron.AttemptNullSignalListSave());
        Assert.Equal(0, neuron.CurrentState.Value);
        Assert.Empty(context.Writes);
    }

    [Fact]
    public async Task NeuronOfTState_SaveWithNullSignalInList_ThrowsArgumentException()
    {
        var neuron = new AdversarialProbeNeuron();
        var context = new RecordingContext(NeuronA1);
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => neuron.AttemptNullSignalInListSave());
        Assert.Contains("is null", ex.Message);
        Assert.Equal(0, neuron.CurrentState.Value);
        Assert.Empty(context.Writes);
    }

    [Fact]
    public async Task NeuronOfTState_SaveWithNullState_ThrowsArgumentNullException()
    {
        var neuron = new AdversarialProbeNeuron();
        var context = new RecordingContext(NeuronA1);
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentNullException>(() => neuron.AttemptNullStateSave());
        Assert.Equal(0, neuron.CurrentState.Value);
        Assert.Empty(context.Writes);
    }

    [Fact]
    public async Task NeuronOfTState_SaveWithNullSingleSignal_ThrowsArgumentNullException()
    {
        var neuron = new AdversarialProbeNeuron();
        var context = new RecordingContext(NeuronA1);
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentNullException>(() => neuron.AttemptNullSignalSave());
        Assert.Equal(0, neuron.CurrentState.Value);
        Assert.Empty(context.Writes);
    }

    [Fact]
    public async Task NeuronOfTState_FailingStore_DoesNotMutateStateOrCallCommitted()
    {
        var neuron = new AdversarialProbeNeuron();
        var failingSeat = new FaultyFailingSeat();
        NeuronAttachment.Attach(neuron, failingSeat);
        await NeuronAttachment.Activate(neuron, CancellationToken.None);

        var initial = neuron.CurrentState;
        Assert.Equal(0, initial.Value);

        await Assert.ThrowsAsync<InvalidOperationException>(() => neuron.Update(55, "should-fail"));

        // State remains intact; OnCommitted was NOT called
        Assert.Equal(0, neuron.CurrentState.Value);
        Assert.Same(initial, neuron.CurrentState);
        Assert.False(neuron.CommittedCalled);
    }

    #endregion

    #region 3. INeuronSeat Lifecycle (Load, Save, Clear) on RecordingContext

    [Fact]
    public async Task INeuronSeat_RecordingContext_LifecycleRoundTrip()
    {
        var context = new RecordingContext(NeuronA1);
        await VerifySeatLifecycle(context);
    }

    private static async Task VerifySeatLifecycle(INeuronSeat seat)
    {
        // 1. Initial Load is null
        var loaded = await seat.Load<SampleNeuronState>();
        Assert.Null(loaded);

        // 2. Save persists state
        var state1 = new SampleNeuronState(100, "first");
        await seat.Save(state1, [new SampleValueChanged(100)]);

        var reloaded = await seat.Load<SampleNeuronState>();
        Assert.NotNull(reloaded);
        Assert.Equal(100, reloaded.Value);
        Assert.Equal("first", reloaded.Label);

        // 3. Clear deletes state
        await seat.Clear();
        var afterClear = await seat.Load<SampleNeuronState>();
        Assert.Null(afterClear);

        // 4. Save after clear works
        var state2 = new SampleNeuronState(200, "second");
        await seat.Save(state2, [new SampleValueChanged(200)]);

        var reloaded2 = await seat.Load<SampleNeuronState>();
        Assert.NotNull(reloaded2);
        Assert.Equal(200, reloaded2.Value);
    }

    #endregion
}
