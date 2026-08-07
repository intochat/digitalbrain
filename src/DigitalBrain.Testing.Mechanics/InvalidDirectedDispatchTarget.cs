namespace DigitalBrain.Testing.Mechanics;

public sealed class InvalidDirectedDispatchTarget : Neuron, INeuron<MechanicsEcho>
{
    public Task HandleAsync(MechanicsEcho synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
