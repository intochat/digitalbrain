namespace DigitalBrain.Runtime.Neurons;

public interface ISynapsePersistenceService
{
    Task SaveSynapseAsync(Synapse synapse, CancellationToken cancellationToken = default);
}
