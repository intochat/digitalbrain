namespace DigitalBrain.Abstractions.Capabilities;

public sealed record SynapseCapabilityDescriptor(
    string ContractId,
    int SchemaVersion,
    string Description,
    string JsonSchema);

