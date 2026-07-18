using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic.Swarm;

public interface ISwarmWorkerNeuron : INeuron
{
    Task RegisterSessionAsync(Guid sessionId);
}
