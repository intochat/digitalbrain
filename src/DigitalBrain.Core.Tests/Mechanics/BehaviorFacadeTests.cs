namespace DigitalBrain;

public sealed class BehaviorFacadeTests
{
    [Fact]
    public void RejectsBehaviorOperationsOutsideABoundTurn()
    {
        var neuron = new ProbeNeuron();

        Assert.Throws<InvalidOperationException>(() => _ = neuron.ReadId());
        Assert.Throws<InvalidOperationException>(() => _ = neuron.ReadOrigin());
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
        var origin = new SynapseOrigin(
            new NeuronId("digitalbrain.synapse-source", "actor/ada"),
            7,
            new DateTimeOffset(2040, 1, 1, 0, 5, 0, TimeSpan.Zero));
        var initial = new ProbeState { Value = "initial" };
        var binding = new RecordingBinding(identity, origin, initial);
        var neuron = new StatefulProbeNeuron();

        neuron.Bind(binding);

        Assert.Equal(identity, neuron.ReadId());
        Assert.Equal(origin, neuron.ReadOrigin());
        Assert.Same(initial, neuron.ReadState());
        neuron.Publish(new ProbeSynapse());
        neuron.ReplaceState(new ProbeState { Value = "replaced" });

        Assert.Single(binding.Staged);
        Assert.IsType<ProbeSynapse>(binding.Staged[0].Synapse);
        Assert.Equal(Dispatch.Broadcast, binding.Staged[0].Dispatch);
        Assert.Equal("replaced", binding.State!.Value);

        neuron.Unbind(binding);

        Assert.Throws<InvalidOperationException>(() => _ = neuron.ReadId());
        Assert.Throws<InvalidOperationException>(() => neuron.Publish(new ProbeSynapse()));
        Assert.Throws<InvalidOperationException>(neuron.ReadState);
    }

    [Fact]
    public void StagesDeclaredDirectDeliverySeparatelyFromBroadcast()
    {
        var binding = new RecordingBinding(
            new NeuronId("probe", "one"),
            new SynapseOrigin(
                new NeuronId("digitalbrain.synapse-source", "actor/ada"),
                1,
                new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new ProbeState());
        var receiver = new NeuronId("receiver", "destination");
        var neuron = new ProbeNeuron();

        neuron.Bind(binding);
        neuron.Publish(new ProbeSynapse());
        neuron.Publish(new ProbeSynapse(), Dispatch.Direct(receiver));

        Assert.Collection(
            binding.Staged,
            broadcast =>
            {
                Assert.IsType<ProbeSynapse>(broadcast.Synapse);
                Assert.Equal(Dispatch.Broadcast, broadcast.Dispatch);
            },
            directed =>
            {
                Assert.IsType<ProbeSynapse>(directed.Synapse);
                Assert.Equal(receiver, directed.Dispatch.Receiver);
            });
    }

    private sealed class ProbeNeuron : Neuron
    {
        internal NeuronId ReadId() => Id;

        internal SynapseOrigin ReadOrigin() => Origin;

        internal void Publish(Synapse synapse) => Emit(synapse);

        internal void Publish(Synapse synapse, Dispatch dispatch) => Emit(synapse, dispatch);
    }

    private sealed class StatefulProbeNeuron : Neuron<ProbeState>
    {
        internal NeuronId ReadId() => Id;

        internal SynapseOrigin ReadOrigin() => Origin;

        internal void Publish(Synapse synapse) => Emit(synapse);

        internal ProbeState ReadState() => State;

        internal void ReplaceState(ProbeState state) => State = state;
    }

    private sealed record ProbeSynapse : Synapse;

    private sealed class ProbeState
    {
        internal string? Value { get; init; }
    }

    private sealed class RecordingBinding(NeuronId id, SynapseOrigin origin, ProbeState initial) : ITurnBinding
    {
        internal List<StagedSynapse> Staged { get; } = [];

        internal ProbeState? State { get; private set; } = initial;

        public NeuronId Id { get; } = id;

        public SynapseOrigin Origin { get; } = origin;

        public void Stage(Synapse synapse, Dispatch dispatch) => Staged.Add(new StagedSynapse(synapse, dispatch));

        public TState GetState<TState>()
            where TState : class, new()
            => (TState)(object)(State ??= new ProbeState());

        public void SetState<TState>(TState state)
            where TState : class, new()
            => State = (ProbeState)(object)state;
    }
}
