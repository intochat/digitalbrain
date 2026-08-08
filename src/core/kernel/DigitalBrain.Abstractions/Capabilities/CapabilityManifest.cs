namespace DigitalBrain.Abstractions;

public sealed record CapabilityManifest(
    ModuleId ModuleId,
    string Version,
    string Description,
    IReadOnlyList<NeuronCapabilityDescriptor> Neurons);

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
