namespace DigitalBrain.Runtime.Neurons;

public interface INeuronMetadata
{
    static abstract NeuronId Id { get; }
    static abstract string Icon { get; }
    static abstract NeuronCapability Capabilities { get; }
}
