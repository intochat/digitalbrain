namespace DigitalBrain.Testing.Mechanics;

public sealed class DirectedDispatchEmitter : Neuron, INeuron<MechanicsStart>
{
    public Task HandleAsync(MechanicsStart synapse, CancellationToken cancellationToken)
    {
        Emit(
            new MechanicsPulse(Echo: false),
            Dispatch.Direct(new NeuronId("directing-receiver", "destination")));
        return Task.CompletedTask;
    }
}
