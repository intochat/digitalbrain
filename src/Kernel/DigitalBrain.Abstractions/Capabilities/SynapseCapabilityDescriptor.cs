namespace DigitalBrain.Abstractions.Capabilities;

public sealed record SynapseCapabilityDescriptor(
    string ContractId,
    int SchemaVersion,
    string Description,
    string JsonSchema,
    // Null unless the synapse type itself declares a DefaultInstanceName const, overriding
    // the host neuron's own default for this one capability (ModuleReflection.DescriptorFor).
    string? DefaultInstanceName = null);

