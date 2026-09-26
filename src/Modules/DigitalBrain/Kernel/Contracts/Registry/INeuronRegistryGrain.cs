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

[Alias("kernel.neuron-registry")]
[Orleans.Metadata.DefaultGrainType("kernel.neuron-registry")]
public interface INeuronRegistryGrain : IGrainWithStringKey
{
    Task Register(NeuronRegistration[] records);
    Task<NeuronRegistration[]> Read();
    Task<NeuronRegistration?> Find(string id);
}
