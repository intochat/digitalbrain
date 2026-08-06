namespace DigitalBrain;

public sealed class BehaviorFacadeTests
{
    [Fact]
    public void RejectsBehaviorOperationsOutsideABoundTurn()
    {
        var neuron = new ProbeNeuron();

        Assert.Throws<InvalidOperationException>(() => _ = neuron.ReadId());
        Assert.Throws<InvalidOperationException>(() => neuron.Publish(new ProbeSynapse()));
    }

    [Fact]
    public void RejectsStateOutsideABoundTurn()
    {
        var neuron = new StatefulProbeNeuron();

        Assert.Throws<InvalidOperationException>(neuron.ReadState);
        Assert.Throws<InvalidOperationException>(() => neuron.ReplaceState(new ProbeState()));
    }

    [Fact]
    public void BindsIdentityStagedSynapsesAndTurnStateOnlyForTheActiveTurn()
    {
        var identity = new NeuronId("probe", "one");
        var initial = new ProbeState { Value = "initial" };
        var binding = new RecordingBinding(identity, initial);
        var neuron = new StatefulProbeNeuron();

        neuron.Bind(binding);

        Assert.Equal(identity, neuron.ReadId());
        Assert.Same(initial, neuron.ReadState());
        neuron.Publish(new ProbeSynapse());
        neuron.ReplaceState(new ProbeState { Value = "replaced" });

        Assert.Single(binding.Staged);
        Assert.IsType<ProbeSynapse>(binding.Staged[0]);
        Assert.Equal("replaced", binding.State!.Value);

        neuron.Unbind(binding);

        Assert.Throws<InvalidOperationException>(() => _ = neuron.ReadId());
        Assert.Throws<InvalidOperationException>(() => neuron.Publish(new ProbeSynapse()));
        Assert.Throws<InvalidOperationException>(neuron.ReadState);
    }

    private sealed class ProbeNeuron : Neuron
    {
        internal NeuronId ReadId() => Id;

        internal void Publish(Synapse synapse) => Emit(synapse);
    }

    private sealed class StatefulProbeNeuron : Neuron<ProbeState>
    {
        internal NeuronId ReadId() => Id;

        internal void Publish(Synapse synapse) => Emit(synapse);

        internal ProbeState ReadState() => State;

        internal void ReplaceState(ProbeState state) => State = state;
    }

    private sealed record ProbeSynapse : Synapse;

    private sealed class ProbeState
    {
        internal string? Value { get; init; }
    }

    private sealed class RecordingBinding(NeuronId id, ProbeState initial) : ITurnBinding
    {
        internal List<Synapse> Staged { get; } = [];

        internal ProbeState? State { get; private set; } = initial;

        public NeuronId Id { get; } = id;

        public void Stage(Synapse synapse) => Staged.Add(synapse);

        public TState GetState<TState>()
            where TState : class, new()
            => (TState)(object)(State ??= new ProbeState());

        public void SetState<TState>(TState state)
            where TState : class, new()
            => State = (ProbeState)(object)state;
    }
}
