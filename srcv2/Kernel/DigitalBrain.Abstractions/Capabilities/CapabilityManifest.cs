namespace DigitalBrain.Abstractions.Capabilities;

public sealed record CapabilityManifest(
    ModuleId ModuleId,
    string Version,
    string Description,
    IReadOnlyList<NeuronCapabilityDescriptor> Neurons,
    IReadOnlyList<SynapseCapabilityDescriptor>? Facts = null)
{
    public IReadOnlyList<SynapseCapabilityDescriptor> Facts { get; init; } = Facts ?? [];
}

