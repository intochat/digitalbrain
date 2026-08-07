namespace DigitalBrain.Testing.Mechanics;

public sealed class RetryEmitter : Neuron, INeuron<RetrySeed>
{
    public Task HandleAsync(RetrySeed synapse, CancellationToken cancellationToken)
    {
        Emit(new RetryPulse(synapse.Key));
        return Task.CompletedTask;
    }

}
