namespace DigitalBrain.Testing.Mechanics;

public sealed class MechanicsReceiver : Neuron, INeuron<MechanicsPulse>
{
    public Task HandleAsync(MechanicsPulse synapse, CancellationToken cancellationToken)
    {
        if (synapse.Echo)
        {
            Emit(new MechanicsEcho());
        }

        return Task.CompletedTask;
    }
}
