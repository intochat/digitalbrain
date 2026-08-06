namespace DigitalBrain.Testing.Mechanics;

public sealed class MechanicsEmitter : Neuron, INeuron<MechanicsStart>, INeuron<MechanicsEcho>
{
    public Task HandleAsync(MechanicsStart synapse, CancellationToken cancellationToken)
    {
        Emit(new MechanicsPulse(synapse.Echo));
        if (synapse.Audit)
        {
            Emit(new MechanicsAudit());
        }

        return Task.CompletedTask;
    }

    public Task HandleAsync(MechanicsEcho synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}
