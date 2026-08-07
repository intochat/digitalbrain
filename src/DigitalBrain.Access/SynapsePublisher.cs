namespace DigitalBrain;

public interface SynapsePublisher
{
    Task PublishAsync(
        Synapse synapse,
        CancellationToken cancellationToken = default);
}
