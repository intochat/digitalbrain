namespace DigitalBrain.Kernel;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class NeuronStateAttribute : Attribute, IFacetMetadata;
