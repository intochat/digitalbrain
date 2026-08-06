namespace DigitalBrain;

public interface SynapsePublisher
{
    Task PublishAsync(
        SynapseSource source,
        Synapse synapse,
        CancellationToken cancellationToken = default);
}
