namespace DigitalBrain.Kernel.Visualization;

public interface INeuronFeatureLoader
{
    (string Text, string SourceFile)? GetFeature(string neuronTypeFullName);
}
