namespace DigitalBrain.Abstractions;

public sealed record SynapseCapabilityDescriptor(
    string ContractId,
    int SchemaVersion,
    string Description,
    string JsonSchema);

