using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;

namespace DigitalBrain.Tests.Unit;

public sealed class CoreVocabularyFacts
{
    [Theory]
    [InlineData("DigitalBrain.Signal")]
    [InlineData("DigitalBrain.Kernel.Synapse")]
    [InlineData("DigitalBrain.Neurons.Neuron")]
    [InlineData("DigitalBrain.Neurons.Neuron`1")]
    [InlineData("DigitalBrain.BrainId")]
    [InlineData("DigitalBrain.NeuronId")]
    [InlineData("DigitalBrain.Neurons.DeactivationCause")]
    [InlineData("DigitalBrain.Neurons.NeuronAttribute")]
    [InlineData("DigitalBrain.SignalAttribute")]
    [InlineData("DigitalBrain.Neurons.OnAttribute")]
    [InlineData("DigitalBrain.IScreen")]
    [InlineData("DigitalBrain.Screen`1")]
    [InlineData("DigitalBrain.CapabilityAttribute")]
    [InlineData("DigitalBrain.Contracts.InterleavesAttribute")]
    [InlineData("DigitalBrain.Neurons.InterleavesWhenAttribute")]
    [InlineData("DigitalBrain.Contracts.ReadOnlyCallAttribute")]
    [InlineData("DigitalBrain.Contracts.OneWayCallAttribute")]
    [InlineData("DigitalBrain.Contracts.CallTimeoutAttribute")]
    [InlineData("DigitalBrain.Neurons.ReentrantAttribute")]
    [InlineData("DigitalBrain.Neurons.INeuronContext")]
    [InlineData("DigitalBrain.Neurons.ITimers")]
    [InlineData("DigitalBrain.Neurons.IReminders")]
    [InlineData("DigitalBrain.Operations.IOperations")]
    [InlineData("DigitalBrain.Kernel.ISynapses")]
    [InlineData("DigitalBrain.Neurons.INeuronProxies")]
    public void Core_exports_the_vocabulary(string name)
    {
        var type = typeof(Signal).Assembly.GetType(name);

        Assert.NotNull(type);
        Assert.True(type.IsPublic, name);
    }

    [Fact]
    public void Synapse_carries_from_signal_and_to()
    {
        var from = NeuronId.Parse("b1/files/a");
        var to = NeuronId.Parse("b1/index/main");

        Assert.Equal(new Synapse(from, typeof(Changed), to), new Synapse(from, typeof(Changed), to));
    }

    [Fact]
    public void Attach_runs_once()
    {
        var neuron = new Counter();
        var context = new NullContext();

        NeuronAttachment.Attach(neuron, context);

        Assert.Throws<InvalidOperationException>(() => NeuronAttachment.Attach(neuron, context));
        Assert.Equal(0, neuron.Value);
        Assert.Equal(context.Id, neuron.Identity);
    }

    [Fact]
    public void Attach_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => NeuronAttachment.Attach(new Counter(), null!));
    }

    [Fact]
    public void Scheduling_attributes_carry_their_values()
    {
        Assert.Equal(1500, new CallTimeoutAttribute(1500).Milliseconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CallTimeoutAttribute(0));
        Assert.Equal("CanInterleave", new InterleavesWhenAttribute("CanInterleave").Predicate);
        Assert.Equal(("storage", 1), (new CapabilityAttribute("storage", 1).Name, new CapabilityAttribute("storage", 1).Version));
    }

    private sealed record Changed : Signal;

    private sealed class CounterState
    {
        public int Value { get; set; }
    }

    private sealed class Counter : Neuron<CounterState>
    {
        public int Value => State.Value;

        public NeuronId Identity => Id;
    }

    private sealed class NullContext : INeuronContext, INeuronSeat
    {
        public NeuronId Id => NeuronId.Parse("b1/counter/a");
        public DigitalBrain.Access.Caller? Caller => null;
        public TimeProvider Time => TimeProvider.System;
        public ITimers Timers => throw new NotSupportedException();
        public IReminders Reminders => throw new NotSupportedException();
        public ISynapses Synapses => throw new NotSupportedException();

        public string? TraceId => null;
        public Task Publish(Signal signal) => Task.CompletedTask;
        public T Get<T>(NeuronId id) where T : class => throw new NotSupportedException();
        public void Defer(Func<Task> work) => throw new NotSupportedException();

        public IServiceProvider Services => throw new NotSupportedException();
        public Task<T?> Load<T>() where T : class => Task.FromResult<T?>(null);
        public Task Save<T>(T next, IReadOnlyList<Signal> signals) where T : class => Task.CompletedTask;
        public Task Clear() => Task.CompletedTask;
    }
}
