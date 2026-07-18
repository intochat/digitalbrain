namespace DigitalBrain.Abstractions.Communication;

public static class ClusterBridge
{
    // Mirrors NeuronNaming.ToGrainType(typeof(ClusterBridgeNeuron)); the neuron implementation
    // lives in a bundle the kernel must not reference, so the brain addresses it by this contract.
    public const string GrainType = "clusterbridge";
}
