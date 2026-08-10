namespace DigitalBrain.Abstractions;

public sealed record CapabilityManifest(
    ModuleId ModuleId,
    string Version,
    string Description,
    IReadOnlyList<NeuronCapabilityDescriptor> Neurons,
    IReadOnlyList<SynapseCapabilityDescriptor>? Facts = null)
{
    public IReadOnlyList<SynapseCapabilityDescriptor> Facts { get; init; } = Facts ?? [];
}

public sealed record NeuronCapabilityDescriptor(
    string ContractId,
    string Description,
    string DefaultInstanceName,
    IReadOnlyList<SynapseCapabilityDescriptor> Accepted,
    IReadOnlyList<SynapseCapabilityDescriptor> Emitted);

public sealed record SynapseCapabilityDescriptor(
    string ContractId,
    int SchemaVersion,
    string Description,
    string JsonSchema);
