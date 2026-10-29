using DigitalBrain.Spi;

namespace DigitalBrain.Tests.Unit;

public sealed class CoreVocabularyFacts
{
    [Theory]
    [InlineData("DigitalBrain.Signal")]
    [InlineData("DigitalBrain.Synapse")]
    [InlineData("DigitalBrain.Neuron")]
    [InlineData("DigitalBrain.Neuron`1")]
    [InlineData("DigitalBrain.BrainId")]
    [InlineData("DigitalBrain.NeuronId")]
    [InlineData("DigitalBrain.DeactivationCause")]
    [InlineData("DigitalBrain.NeuronAttribute")]
    [InlineData("DigitalBrain.SignalAttribute")]
    [InlineData("DigitalBrain.OnAttribute")]
    [InlineData("DigitalBrain.AppAttribute")]
    [InlineData("DigitalBrain.CapabilityAttribute")]
    [InlineData("DigitalBrain.InterleavesAttribute")]
    [InlineData("DigitalBrain.InterleavesWhenAttribute")]
    [InlineData("DigitalBrain.ReadOnlyCallAttribute")]
    [InlineData("DigitalBrain.OneWayCallAttribute")]
    [InlineData("DigitalBrain.CallTimeoutAttribute")]
    [InlineData("DigitalBrain.ReentrantAttribute")]
    [InlineData("DigitalBrain.Spi.INeuronContext")]
    [InlineData("DigitalBrain.Spi.INeuronState")]
    [InlineData("DigitalBrain.Spi.ITimers")]
    [InlineData("DigitalBrain.Spi.IReminders")]
    [InlineData("DigitalBrain.Spi.IOperations")]
    [InlineData("DigitalBrain.Spi.ISynapses")]
    [InlineData("DigitalBrain.Spi.INeuronProxies")]
    public void Core_exports_the_vocabulary(string name)
    {
        var type = typeof(Signal).Assembly.GetType(name);

        Assert.NotNull(type);
        Assert.True(type.IsPublic, name);
    }

    [Fact]
    public void Spi_is_interfaces_only()
    {
        var spi = typeof(Signal).Assembly.GetExportedTypes().Where(t => t.Namespace == "DigitalBrain.Spi").ToList();

        Assert.NotEmpty(spi);
        // Interfaces, plus static entry points a runtime calls (NeuronAttachment).
        Assert.All(spi, t => Assert.True(t.IsInterface || (t.IsAbstract && t.IsSealed), t.FullName));
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

    private sealed class NullContext : INeuronContext
    {
        public NeuronId Id => NeuronId.Parse("b1/counter/a");
        public IServiceProvider Services => throw new NotSupportedException();
        public TimeProvider Time => TimeProvider.System;
        public INeuronState State => throw new NotSupportedException();
        public ITimers Timers => throw new NotSupportedException();
        public IReminders Reminders => throw new NotSupportedException();
        public IOperations Operations => throw new NotSupportedException();
        public DigitalBrain.Confirmation.IConfirmations Confirmations => throw new NotSupportedException();
        public ISynapses Synapses => throw new NotSupportedException();
        public ISignalHistory History => throw new NotSupportedException();
        public Task Publish(Signal signal) => Task.CompletedTask;
        public T Get<T>(NeuronId id) where T : class => throw new NotSupportedException();
        public void Defer(Func<Task> work) => throw new NotSupportedException();
    }
}
