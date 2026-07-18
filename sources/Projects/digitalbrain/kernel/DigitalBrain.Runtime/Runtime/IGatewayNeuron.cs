using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Runtime;

[Orleans.Metadata.DefaultGrainType("GatewayNeuron")]
public interface IGatewayNeuron : IGrainWithGuidKey
{
    Task RouteAsync(Synapse synapse);
    Task EnsureActivatedAsync();
}
