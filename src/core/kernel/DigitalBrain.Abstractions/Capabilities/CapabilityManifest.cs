namespace DigitalBrain.Abstractions;

public sealed class CapabilityManifest
{
    public CapabilityManifest(
        ModuleId moduleId,
        string version,
        string description,
        IReadOnlyList<string> configurationKeys,
        IReadOnlyList<NeuronCapabilityDescriptor> neurons)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(configurationKeys);
        ArgumentNullException.ThrowIfNull(neurons);

        ModuleId = moduleId;
        Version = version;
        Description = description;
        ConfigurationKeys = configurationKeys;
        Neurons = neurons;
    }

    public ModuleId ModuleId { get; }

    public string Version { get; }

    public string Description { get; }

    public IReadOnlyList<string> ConfigurationKeys { get; }

    public IReadOnlyList<NeuronCapabilityDescriptor> Neurons { get; }
}

public sealed class NeuronCapabilityDescriptor
{
    public NeuronCapabilityDescriptor(
        string contractId,
        string description,
        string defaultInstanceName,
        IReadOnlyList<SynapseCapabilityDescriptor> accepted,
        IReadOnlyList<SynapseCapabilityDescriptor> emitted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultInstanceName);
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(emitted);

        ContractId = contractId;
        Description = description;
        DefaultInstanceName = defaultInstanceName;
        Accepted = accepted;
        Emitted = emitted;
    }

    public string ContractId { get; }

    public string Description { get; }

    public string DefaultInstanceName { get; }

    public IReadOnlyList<SynapseCapabilityDescriptor> Accepted { get; }

    public IReadOnlyList<SynapseCapabilityDescriptor> Emitted { get; }
}

public sealed class SynapseCapabilityDescriptor
{
    public SynapseCapabilityDescriptor(
        string contractId,
        int schemaVersion,
        string description,
        string jsonSchema,
        IReadOnlyList<string> examples)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        ArgumentOutOfRangeException.ThrowIfLessThan(schemaVersion, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonSchema);
        ArgumentNullException.ThrowIfNull(examples);

        ContractId = contractId;
        SchemaVersion = schemaVersion;
        Description = description;
        JsonSchema = jsonSchema;
        Examples = examples;
    }

    public string ContractId { get; }

    public int SchemaVersion { get; }

    public string Description { get; }

    public string JsonSchema { get; }

    public IReadOnlyList<string> Examples { get; }
}
