namespace DigitalBrain.Abstractions.Capabilities;

public sealed record NeuronCapabilityDescriptor(
    string ContractId,
    string Description,
    string DefaultInstanceName,
    IReadOnlyList<SynapseCapabilityDescriptor> Accepted,
    IReadOnlyList<SynapseCapabilityDescriptor> Emitted);

