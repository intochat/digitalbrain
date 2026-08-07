namespace DigitalBrain.Testing.Mechanics;

public sealed class InvalidDirectedDispatchEmitter : Neuron, INeuron<MechanicsStart>
{
    public Task HandleAsync(MechanicsStart synapse, CancellationToken cancellationToken)
    {
        if (synapse.Echo)
        {
            Emit(new MechanicsAudit());
        }
        else
        {
            Emit(
                new MechanicsPulse(Echo: false),
                Dispatch.Direct(new NeuronId("invalid-directed-target", "destination")));
        }

        return Task.CompletedTask;
    }
}
