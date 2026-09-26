namespace DigitalBrain.Contracts.Registry;

[GenerateSerializer, Alias("kernel.registry.neuron")]
public sealed record NeuronRegistration
{
    [Id(0)] public required string Id { get; init; }
    [Id(1)] public required string ContractType { get; init; }
    [Id(2)] public required string ModuleId { get; init; }
    [Id(3)] public required string Name { get; init; }
    [Id(4)] public required string Description { get; init; }
    [Id(5)] public required bool AgentRoutable { get; init; }
}

[GenerateSerializer, Alias("kernel.registry.snapshot")]
public sealed record NeuronRegistrySnapshot
{
    [Id(0)] public required string Version { get; init; }
    [Id(1)] public NeuronRegistration[] Records { get; init; } = [];
}

[Alias("kernel.neuron-registry")]
[Orleans.Metadata.DefaultGrainType("kernel.neuron-registry")]
public interface INeuronRegistryGrain : IGrainWithStringKey
{
    Task ReplaceSnapshot(NeuronRegistrySnapshot snapshot);
    Task<NeuronRegistrySnapshot> Read();
    Task<NeuronRegistration?> Find(string id);
}
