namespace DigitalBrain.Testing.Mechanics;

public sealed class DirectedDispatchReceiver : Neuron, INeuron<MechanicsPulse>
{
    public Task HandleAsync(MechanicsPulse synapse, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
